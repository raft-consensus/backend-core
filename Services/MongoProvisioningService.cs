using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using raft_backend.Configuration;
using raft_backend.DTOs;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public class MongoProvisioningService : IDatabaseProvisioningService
{
    public string Engine => "MongoDB";

    private readonly IUserDashboardService _dashboardService;
    private readonly IAccessCredentialService _accessCredentialService;
    private readonly IAuditEventService _auditEventService;
    private readonly IDatabaseInstanceService _databaseInstanceService;
    private readonly ISecurePasswordGenerator _passwordGenerator;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly MongoProvisioningOptions _options;
    private readonly ExternalCellConnectionStrings _connectionStrings;
    private readonly ILogger<MongoProvisioningService> _logger;

    public MongoProvisioningService(
        IUserDashboardService dashboardService,
        IAccessCredentialService accessCredentialService,
        IAuditEventService auditEventService,
        IDatabaseInstanceService databaseInstanceService,
        ISecurePasswordGenerator passwordGenerator,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<MongoProvisioningOptions> options,
        IOptions<ExternalCellConnectionStrings> connectionStrings,
        ILogger<MongoProvisioningService> logger)
    {
        _dashboardService = dashboardService;
        _accessCredentialService = accessCredentialService;
        _auditEventService = auditEventService;
        _databaseInstanceService = databaseInstanceService;
        _passwordGenerator = passwordGenerator;
        _dataProtectionProvider = dataProtectionProvider;
        _options = options.Value;
        _connectionStrings = connectionStrings.Value;
        _logger = logger;
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_connectionStrings.MongoProvisioning);

    public int MaxDatabasesPerUser => _options.MaxDatabasesPerUser;

    public async Task<DatabaseProvisioningResultDto> ProvisionDatabaseAsync(int userId, CancellationToken cancellationToken = default)
    {
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var identifier = $"raft_mg_u{userId}_{suffix}";
        var password = _passwordGenerator.Generate(_options.PasswordLength);

        var connString = _connectionStrings.MongoProvisioning;
        if (string.IsNullOrWhiteSpace(connString))
        {
            throw new InvalidOperationException("MongoProvisioning connection string is missing.");
        }

        var client = new MongoClient(connString);
        var targetDb = client.GetDatabase(identifier);

        var createUserCommand = new BsonDocument
        {
            { "createUser", identifier },
            { "pwd", password },
            { "roles", new BsonArray
                {
                    new BsonDocument
                    {
                        { "role", "readWrite" },
                        { "db", identifier }
                    }
                }
            }
        };

        await targetDb.RunCommandAsync<BsonDocument>(createUserCommand, cancellationToken: cancellationToken);

        var protector = _dataProtectionProvider.CreateProtector(DataProtectionPurposes.AccessCredentialPassword);
        var encryptedPassword = protector.Protect(password);

        var instance = await _databaseInstanceService.CreateAsync(new DatabaseInstanceCreateDto
        {
            UserId = userId,
            Host = _options.PublicHost,
            Port = _options.PublicPort,
            DatabaseName = identifier,
            DatabaseUser = identifier,
            Engine = Engine,
            Status = "Active",
            UsedSpaceBytes = 0,
            MaxSpaceBytes = _options.DefaultMaxSpaceBytes,
            LastActivity = DateTime.UtcNow
        }, cancellationToken) ?? throw new InvalidOperationException("Failed to persist MongoDB database instance.");

        await _accessCredentialService.CreateAsync(new AccessCredentialCreateDto
        {
            DatabaseInstanceId = instance.Id,
            EncryptedPassword = encryptedPassword
        }, cancellationToken);

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = userId,
            EventType = "Provisioning",
            Description = $"MongoDB database '{identifier}' provisioned successfully."
        }, cancellationToken);

        return new DatabaseProvisioningResultDto
        {
            DatabaseInstanceId = instance.Id,
            Host = _options.PublicHost,
            Port = _options.PublicPort,
            DatabaseName = identifier,
            DatabaseUser = identifier,
            Password = password,
            Engine = Engine
        };
    }

    public async Task PauseAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        if (!string.IsNullOrWhiteSpace(_connectionStrings.MongoProvisioning))
        {
            try
            {
                var client = new MongoClient(_connectionStrings.MongoProvisioning);
                var targetDb = client.GetDatabase(instance.DatabaseName);

                // Cambiar el rol del usuario a solo lectura (read)
                var updateUserCommand = new BsonDocument
                {
                    { "updateUser", instance.DatabaseUser },
                    { "roles", new BsonArray
                        {
                            new BsonDocument
                            {
                                { "role", "read" },
                                { "db", instance.DatabaseName }
                            }
                        }
                    }
                };

                await targetDb.RunCommandAsync<BsonDocument>(updateUserCommand, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set read-only role on MongoDB database {DatabaseName}", instance.DatabaseName);
            }
        }

        await UpdateStatusAsync(databaseInstanceId, instance, "Suspended", cancellationToken);
    }

    public async Task ResumeAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        if (!string.IsNullOrWhiteSpace(_connectionStrings.MongoProvisioning))
        {
            try
            {
                var client = new MongoClient(_connectionStrings.MongoProvisioning);
                var targetDb = client.GetDatabase(instance.DatabaseName);

                // Restaurar el rol del usuario a lectura y escritura (readWrite)
                var updateUserCommand = new BsonDocument
                {
                    { "updateUser", instance.DatabaseUser },
                    { "roles", new BsonArray
                        {
                            new BsonDocument
                            {
                                { "role", "readWrite" },
                                { "db", instance.DatabaseName }
                            }
                        }
                    }
                };

                await targetDb.RunCommandAsync<BsonDocument>(updateUserCommand, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore readWrite role on MongoDB database {DatabaseName}", instance.DatabaseName);
            }
        }

        await UpdateStatusAsync(databaseInstanceId, instance, "Active", cancellationToken);
    }

    public async Task DeleteAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        if (!string.IsNullOrWhiteSpace(_connectionStrings.MongoProvisioning))
        {
            try
            {
                var client = new MongoClient(_connectionStrings.MongoProvisioning);
                var targetDb = client.GetDatabase(instance.DatabaseName);

                // Eliminar el usuario de MongoDB para revocarle todo acceso de inmediato
                var dropUserCommand = new BsonDocument
                {
                    { "dropUser", instance.DatabaseUser }
                };

                await targetDb.RunCommandAsync<BsonDocument>(dropUserCommand, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to drop MongoDB user {DatabaseUser} from {DatabaseName}", instance.DatabaseUser, instance.DatabaseName);
            }
        }

        await _databaseInstanceService.SoftDeleteAsync(databaseInstanceId, cancellationToken);
    }

    public async Task PurgeAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        if (!string.IsNullOrWhiteSpace(_connectionStrings.MongoProvisioning))
        {
            try
            {
                var client = new MongoClient(_connectionStrings.MongoProvisioning);
                await client.DropDatabaseAsync(instance.DatabaseName, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to drop MongoDB database {DatabaseName}", instance.DatabaseName);
            }
        }

        await UpdateStatusAsync(databaseInstanceId, instance, "Deleted", cancellationToken);
    }

    public async Task<long> GetUsedSpaceBytesAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionStrings.MongoProvisioning))
        {
            return 0L;
        }

        try
        {
            var client = new MongoClient(_connectionStrings.MongoProvisioning);
            var targetDb = client.GetDatabase(databaseName);
            var stats = await targetDb.RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1), cancellationToken: cancellationToken);

            if (stats.Contains("dataSize"))
            {
                return stats["dataSize"].ToInt64();
            }
            if (stats.Contains("storageSize"))
            {
                return stats["storageSize"].ToInt64();
            }
            if (stats.Contains("totalSize"))
            {
                return stats["totalSize"].ToInt64();
            }

            return 0L;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate used space for MongoDB database {DatabaseName}", databaseName);
            return 0L;
        }
    }

    public async Task<int> GetActiveConnectionCountAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionStrings.MongoProvisioning))
        {
            return 0;
        }

        try
        {
            var client = new MongoClient(_connectionStrings.MongoProvisioning);
            var adminDb = client.GetDatabase("admin");
            var status = await adminDb.RunCommandAsync<BsonDocument>(new BsonDocument("serverStatus", 1), cancellationToken: cancellationToken);

            if (status.Contains("connections") && status["connections"].AsBsonDocument.Contains("current"))
            {
                return status["connections"]["current"].ToInt32();
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private async Task UpdateStatusAsync(
        int databaseInstanceId,
        DatabaseInstanceReadDto instance,
        string status,
        CancellationToken cancellationToken)
    {
        await _databaseInstanceService.UpdateAsync(
            databaseInstanceId,
            new DatabaseInstanceUpdateDto
            {
                UserId = instance.UserId,
                Host = instance.Host,
                Port = instance.Port,
                DatabaseName = instance.DatabaseName,
                DatabaseUser = instance.DatabaseUser,
                Engine = instance.Engine,
                Status = status,
                UsedSpaceBytes = instance.UsedSpaceBytes,
                MaxSpaceBytes = instance.MaxSpaceBytes,
                LastActivity = instance.LastActivity
            },
            cancellationToken);
    }
}
