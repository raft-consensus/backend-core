using System.Data.Common;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.Database;

namespace raft_backend.Services;

// Runs on a timer instead of a SQL Server Agent job so it works regardless of SQL Server
// edition (Express has no Agent). The *decision* of who's due for pause/delete/over-quota is
// entirely SQL-side (see Database/sql-server-schema.md, section 8) — this class only asks
// "who" and executes the already-decided, mechanical MySQL actions.
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
        // BackgroundService is a singleton; RaftDbContext/MySqlDbContext and everything built
        // on top of them are scoped — a fresh DI scope per tick is required.
        using var scope = _scopeFactory.CreateScope();
        var mySqlExecutor = scope.ServiceProvider.GetRequiredService<IMySqlCommandExecutor>();
        var sqlExecutor = scope.ServiceProvider.GetRequiredService<ISqlStoredProcedureExecutor>();
        var databaseInstanceService = scope.ServiceProvider.GetRequiredService<IDatabaseInstanceService>();
        var provisioningService = scope.ServiceProvider.GetRequiredService<IMySqlProvisioningService>();

        await TouchActiveConnectionsAsync(mySqlExecutor, sqlExecutor, cancellationToken);
        await PauseInactiveAsync(sqlExecutor, provisioningService, cancellationToken);
        await DeleteExpiredAsync(sqlExecutor, provisioningService, cancellationToken);
        await RecalculateStorageAsync(mySqlExecutor, sqlExecutor, databaseInstanceService, provisioningService, cancellationToken);
    }

    // Coarse, poll-based activity measurement: granularity equals the tick interval. Good
    // enough for a 7/30-day TTL window; a precise events_statements_summary approach is a
    // future improvement, not required here.
    private async Task TouchActiveConnectionsAsync(
        IMySqlCommandExecutor mySqlExecutor,
        ISqlStoredProcedureExecutor sqlExecutor,
        CancellationToken cancellationToken)
    {
        var activeUsers = await mySqlExecutor.QueryAsync(
            "SELECT DISTINCT PROCESSLIST_USER FROM performance_schema.threads WHERE PROCESSLIST_USER IS NOT NULL AND PROCESSLIST_USER <> @ProvisioningAccount",
            command => command.AddParameter("@ProvisioningAccount", ProvisioningAccount),
            reader => reader.GetString(0),
            cancellationToken);

        foreach (var databaseUser in activeUsers)
        {
            await sqlExecutor.ExecuteAsync(
                StoredProcedureNames.DatabaseInstances_TouchActivityByDatabaseUser,
                command => command.AddParameter("@DatabaseUser", databaseUser),
                cancellationToken);
        }
    }

    private async Task PauseInactiveAsync(
        ISqlStoredProcedureExecutor sqlExecutor,
        IMySqlProvisioningService provisioningService,
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
                await provisioningService.PauseAsync(id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pause database instance {Id} for inactivity", id);
            }
        }
    }

    private async Task DeleteExpiredAsync(
        ISqlStoredProcedureExecutor sqlExecutor,
        IMySqlProvisioningService provisioningService,
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
                await provisioningService.DeleteAsync(id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete expired database instance {Id}", id);
            }
        }
    }

    // Reactive, periodic quota enforcement — the honest answer given students write directly
    // to MySQL from their own apps. There is no native way to block a write in real time
    // without this backend proxying all student SQL traffic, which is out of scope.
    private async Task RecalculateStorageAsync(
        IMySqlCommandExecutor mySqlExecutor,
        ISqlStoredProcedureExecutor sqlExecutor,
        IDatabaseInstanceService databaseInstanceService,
        IMySqlProvisioningService provisioningService,
        CancellationToken cancellationToken)
    {
        var usageRows = await mySqlExecutor.QueryAsync(
            "SELECT table_schema, SUM(data_length + index_length) FROM information_schema.TABLES WHERE table_schema LIKE 'raft\\_u%' GROUP BY table_schema",
            null,
            MapSchemaUsage,
            cancellationToken);

        var usageBySchema = usageRows.ToDictionary(x => x.SchemaName, x => x.TotalBytes, StringComparer.Ordinal);
        var instances = await databaseInstanceService.GetAllAsync(cancellationToken);

        foreach (var instance in instances)
        {
            if (instance.Status is not ("Active" or "Suspended"))
            {
                continue;
            }

            var usedBytes = usageBySchema.GetValueOrDefault(instance.DatabaseName, 0L);

            await sqlExecutor.ExecuteAsync(
                StoredProcedureNames.DatabaseInstances_UpdateUsedSpace,
                command =>
                {
                    command.AddParameter("@Id", instance.Id);
                    command.AddParameter("@UsedSpaceBytes", usedBytes);
                },
                cancellationToken);

            if (instance.Status == "Active" && usedBytes > instance.MaxSpaceBytes)
            {
                try
                {
                    await provisioningService.PauseAsync(instance.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to lock over-quota database instance {Id}", instance.Id);
                }
            }
        }
    }

    private static (string SchemaName, long TotalBytes) MapSchemaUsage(DbDataReader reader)
    {
        return (reader.GetString(0), reader.IsDBNull(1) ? 0L : Convert.ToInt64(reader.GetValue(1)));
    }
}
