using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.Database;
using raft_backend.DTOs;
using raft_backend.Interfaces;

namespace raft_backend.Services;

// Runs on a timer instead of a SQL Server Agent job so it works regardless of engine.
// The business decision of who is due for pause/delete still lives in SQL-side metadata
// (the Raft catalog); this class only asks "who" and executes engine-specific mechanical
// actions through the resolved provisioning service.
public class DatabaseLifecycleBackgroundService : BackgroundService
{
    private const string ProvisioningAccount = "raft_provisioner";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LifecycleJobOptions _options;
    private readonly ILogger<DatabaseLifecycleBackgroundService> _logger;

    public DatabaseLifecycleBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<LifecycleJobOptions> options,
        ILogger<DatabaseLifecycleBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.IntervalMinutes));

        do
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database lifecycle tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunTickAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sqlExecutor = scope.ServiceProvider.GetRequiredService<ISqlStoredProcedureExecutor>();
        var databaseInstanceService = scope.ServiceProvider.GetRequiredService<IDatabaseInstanceService>();
        var auditEventService = scope.ServiceProvider.GetRequiredService<IAuditEventService>();
        var resolver = scope.ServiceProvider.GetRequiredService<IDatabaseProvisioningServiceResolver>();

        await PauseInactiveAsync(sqlExecutor, resolver, auditEventService, cancellationToken);
        await DeleteExpiredAsync(sqlExecutor, resolver, auditEventService, cancellationToken);
        await RecalculateStorageAsync(sqlExecutor, databaseInstanceService, resolver, auditEventService, cancellationToken);
    }

    private async Task PauseInactiveAsync(
        ISqlStoredProcedureExecutor sqlExecutor,
        IDatabaseProvisioningServiceResolver resolver,
        IAuditEventService auditEventService,
        CancellationToken cancellationToken)
    {
        var dueForPause = await sqlExecutor.QueryAsync(
            StoredProcedureNames.DatabaseInstances_GetDueForPause,
            command => command.AddParameter("@InactivityDays", _options.InactivityPauseDays),
            reader => reader.GetInt32Value("Id"),
            cancellationToken);

        foreach (var id in dueForPause)
        {
            try
            {
                var instance = await FindInstanceAsync(id, cancellationToken);
                if (instance is null)
                {
                    continue;
                }

                await resolver.Resolve(instance.Engine).PauseAsync(id, cancellationToken);

                await auditEventService.CreateAsync(new AuditEventCreateDto
                {
                    UserId = null,
                    EventType = "DatabasePausedForInactivity",
                    Description = $"Database instance {id} was paused automatically for inactivity."
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pause database instance {Id} for inactivity", id);
            }
        }
    }

    private async Task DeleteExpiredAsync(
        ISqlStoredProcedureExecutor sqlExecutor,
        IDatabaseProvisioningServiceResolver resolver,
        IAuditEventService auditEventService,
        CancellationToken cancellationToken)
    {
        var dueForDelete = await sqlExecutor.QueryAsync(
            StoredProcedureNames.DatabaseInstances_GetDueForDelete,
            command => command.AddParameter("@InactivityDays", _options.InactivityDeleteDays),
            reader => reader.GetInt32Value("Id"),
            cancellationToken);

        foreach (var id in dueForDelete)
        {
            try
            {
                var instance = await FindInstanceAsync(id, cancellationToken);
                if (instance is null)
                {
                    continue;
                }

                await resolver.Resolve(instance.Engine).DeleteAsync(id, cancellationToken);

                await auditEventService.CreateAsync(new AuditEventCreateDto
                {
                    UserId = null,
                    EventType = "DatabaseDeletedForInactivity",
                    Description = $"Database instance {id} was deleted automatically after inactivity TTL."
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete expired database instance {Id}", id);
            }
        }
    }

    private async Task RecalculateStorageAsync(
        ISqlStoredProcedureExecutor sqlExecutor,
        IDatabaseInstanceService databaseInstanceService,
        IDatabaseProvisioningServiceResolver resolver,
        IAuditEventService auditEventService,
        CancellationToken cancellationToken)
    {
        var instances = await databaseInstanceService.GetAllAsync(cancellationToken);

        foreach (var instance in instances)
        {
            var service = ResolveService(instance.Engine, resolver);
            if (service is null || !service.IsAvailable)
            {
                continue;
            }

            var usedBytes = await service.GetUsedSpaceBytesAsync(instance.DatabaseName, cancellationToken);
            var activeConnections = await service.GetActiveConnectionCountAsync(instance.DatabaseName, cancellationToken);

            await sqlExecutor.ExecuteAsync(
                StoredProcedureNames.DatabaseInstances_UpdateUsedSpace,
                command =>
                {
                    command.AddParameter("@Id", instance.Id);
                    command.AddParameter("@UsedSpaceBytes", usedBytes);
                },
                cancellationToken);

            if (activeConnections > 0)
            {
                await sqlExecutor.ExecuteAsync(
                    StoredProcedureNames.DatabaseInstances_TouchActivityByDatabaseName,
                    command => command.AddParameter("@DatabaseName", instance.DatabaseName),
                    cancellationToken);
            }

            if (instance.Status != "Active")
            {
                continue;
            }

            if (usedBytes > instance.MaxSpaceBytes)
            {
                try
                {
                    await service.PauseAsync(instance.Id, cancellationToken);
                    await auditEventService.CreateAsync(new AuditEventCreateDto
                    {
                        UserId = instance.UserId,
                        EventType = "DatabasePausedForQuota",
                        Description = $"Database instance {instance.Id} was paused automatically because it exceeded its storage quota."
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to lock over-quota database instance {Id}", instance.Id);
                }
            }
            else if (activeConnections > _options.MaxConcurrentConnectionsPerDatabase)
            {
                try
                {
                    await service.PauseAsync(instance.Id, cancellationToken);
                    await auditEventService.CreateAsync(new AuditEventCreateDto
                    {
                        UserId = instance.UserId,
                        EventType = "DatabasePausedForConnections",
                        Description = $"Database instance {instance.Id} was paused automatically because it exceeded the concurrent connection limit."
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to lock database instance {Id} for excessive concurrent connections", instance.Id);
                }
            }
        }
    }

    private static IDatabaseProvisioningService? ResolveService(string engine, IDatabaseProvisioningServiceResolver resolver)
    {
        try
        {
            return resolver.Resolve(engine);
        }
        catch
        {
            return null;
        }
    }

    private async Task<DatabaseInstanceReadDto?> FindInstanceAsync(int id, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var databaseInstanceService = scope.ServiceProvider.GetRequiredService<IDatabaseInstanceService>();
        return await databaseInstanceService.GetByIdAsync(id, cancellationToken);
    }
}
