using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.Database;
using raft_backend.DTOs;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public partial class SqlServerProvisioningService : ISqlServerProvisioningService
{
    private const int MaxProvisioningAttempts = 3;

    private readonly ISqlServerCommandExecutor _sqlServerExecutor;
    private readonly ISqlStoredProcedureExecutor _sqlExecutor;
    private readonly ISecurePasswordGenerator _passwordGenerator;
    private readonly IDatabaseInstanceService _databaseInstanceService;
    private readonly IAccessCredentialService _accessCredentialService;
    private readonly IAuditEventService _auditEventService;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly SqlServerProvisioningOptions _options;
    private readonly ILogger<SqlServerProvisioningService> _logger;

    public SqlServerProvisioningService(
        ISqlServerCommandExecutor sqlServerExecutor,
        ISqlStoredProcedureExecutor sqlExecutor,
        ISecurePasswordGenerator passwordGenerator,
        IDatabaseInstanceService databaseInstanceService,
        IAccessCredentialService accessCredentialService,
        IAuditEventService auditEventService,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<SqlServerProvisioningOptions> options,
        ILogger<SqlServerProvisioningService> logger)
    {
        _sqlServerExecutor = sqlServerExecutor;
        _sqlExecutor = sqlExecutor;
        _passwordGenerator = passwordGenerator;
        _databaseInstanceService = databaseInstanceService;
        _accessCredentialService = accessCredentialService;
        _auditEventService = auditEventService;
        _dataProtectionProvider = dataProtectionProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SqlServerProvisioningResultDto> ProvisionDatabaseAsync(int userId, CancellationToken cancellationToken = default)
    {
        var sharedState = await GetSharedProvisioningStateAsync(userId, cancellationToken);
        var protector = _dataProtectionProvider.CreateProtector(DataProtectionPurposes.AccessCredentialPassword);
        var sharedLoginName = sharedState.SharedLoginName;
        var password = sharedState.EncryptedPassword is null
            ? _passwordGenerator.Generate(_options.PasswordLength)
            : protector.Unprotect(sharedState.EncryptedPassword);
        for (var attempt = 1; attempt <= MaxProvisioningAttempts; attempt++)
        {
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            var identifier = $"raft_u{userId}_{suffix}";
            ValidateIdentifier(identifier);
            var loginExists = await LoginExistsAsync(sharedLoginName, cancellationToken);

            try
            {
                await CreateSqlServerDatabaseAndLoginAsync(
                    identifier,
                    sharedLoginName,
                    password,
                    loginExists,
                    sharedState.HasExistingDatabases,
                    cancellationToken);
            }
            catch (Exception ex) when (attempt < MaxProvisioningAttempts)
            {
                _logger.LogWarning(ex, "SQL Server provisioning attempt {Attempt} failed for {Identifier}; retrying with a new identifier", attempt, identifier);
                continue;
            }

            try
            {
                var encryptedPassword = protector.Protect(password);

                var instance = await _databaseInstanceService.CreateAsync(new DatabaseInstanceCreateDto
                {
                    UserId = userId,
                    Host = _options.PublicHost,
                    Port = _options.PublicPort,
                    DatabaseName = identifier,
                    DatabaseUser = sharedLoginName,
                    Engine = "SQL Server",
                    Status = "Active",
                    UsedSpaceBytes = 0,
                    MaxSpaceBytes = _options.DefaultMaxSpaceBytes,
                    LastActivity = null
                }, cancellationToken) ?? throw new InvalidOperationException("Failed to persist the provisioned database instance.");

                await _accessCredentialService.CreateAsync(new AccessCredentialCreateDto
                {
                    DatabaseInstanceId = instance.Id,
                    EncryptedPassword = encryptedPassword
                }, cancellationToken);

                await _auditEventService.CreateAsync(new AuditEventCreateDto
                {
                    UserId = userId,
                    EventType = "Provisioning",
                    Description = $"SQL Server database '{identifier}' provisioned through self-service."
                }, cancellationToken);

                return new SqlServerProvisioningResultDto
                {
                    DatabaseInstanceId = instance.Id,
                    Host = _options.PublicHost,
                    Port = _options.PublicPort,
                    DatabaseName = identifier,
                    DatabaseUser = sharedLoginName,
                    Password = password,
                    Engine = "SQL Server"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist provisioning result for {Identifier}; rolling back SQL Server side", identifier);
                await CleanupSqlServerDatabaseAndLoginAsync(identifier, sharedLoginName, !sharedState.HasExistingDatabases, CancellationToken.None);
                throw;
            }
        }

        throw new InvalidOperationException("SQL Server database provisioning failed after multiple attempts.");
    }

    public async Task PauseAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        if (!await DatabaseExistsAsync(instance.DatabaseName, cancellationToken))
        {
            _logger.LogWarning("Database {DatabaseName} is missing while pausing instance {DatabaseInstanceId}; cleaning up orphaned metadata.", instance.DatabaseName, databaseInstanceId);
            await DeleteAsync(databaseInstanceId, cancellationToken);
            return;
        }

        await SetDatabaseConnectPermissionAsync(instance.DatabaseName, instance.DatabaseUser, allowConnect: false, cancellationToken);
        await UpdateStatusAsync(databaseInstanceId, "Suspended", cancellationToken);
    }

    public async Task ResumeAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        if (!await DatabaseExistsAsync(instance.DatabaseName, cancellationToken))
        {
            _logger.LogWarning("Database {DatabaseName} is missing while resuming instance {DatabaseInstanceId}; cleaning up orphaned metadata.", instance.DatabaseName, databaseInstanceId);
            await DeleteAsync(databaseInstanceId, cancellationToken);
            return;
        }

        await SetDatabaseConnectPermissionAsync(instance.DatabaseName, instance.DatabaseUser, allowConnect: true, cancellationToken);
        await UpdateStatusAsync(databaseInstanceId, "Active", cancellationToken);
    }

    public async Task DeleteAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        await _sqlServerExecutor.ExecuteNonQueryAsync(
            $"""
             IF DB_ID(N'{instance.DatabaseName}') IS NOT NULL
             BEGIN
                 ALTER DATABASE [{instance.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                 DROP DATABASE [{instance.DatabaseName}];
             END
             """,
            null,
            cancellationToken);

        var cleanupState = await GetSharedLoginCleanupStateAsync(instance.UserId, cancellationToken);

        if (cleanupState.CanDropLogin)
        {
            await _sqlServerExecutor.ExecuteNonQueryAsync(
                $"""
                 IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{cleanupState.SharedLoginName}')
                 BEGIN
                     DROP LOGIN [{cleanupState.SharedLoginName}];
                 END
                 """,
                null,
                cancellationToken);
        }

        await _databaseInstanceService.SoftDeleteAsync(databaseInstanceId, cancellationToken);
    }

    private async Task UpdateStatusAsync(int databaseInstanceId, string status, CancellationToken cancellationToken)
    {
        await _sqlExecutor.ExecuteAsync(
            StoredProcedureNames.DatabaseInstances_UpdateStatus,
            command =>
            {
                command.AddParameter("@Id", databaseInstanceId);
                command.AddParameter("@Status", status);
            },
            cancellationToken);
    }

    private async Task CreateSqlServerDatabaseAndLoginAsync(
        string identifier,
        string loginName,
        string password,
        bool loginExists,
        bool hasExistingDatabases,
        CancellationToken cancellationToken)
    {
        try
        {
            await _sqlServerExecutor.ExecuteNonQueryAsync($"CREATE DATABASE [{identifier}]", null, cancellationToken);

            await EnsureSharedLoginAsync(loginName, password, loginExists, hasExistingDatabases, cancellationToken);

            await _sqlServerExecutor.ExecuteNonQueryAsync(
                $"""
                 USE [{identifier}];
                 CREATE USER [{loginName}] FOR LOGIN [{loginName}];
                 ALTER ROLE [db_owner] ADD MEMBER [{loginName}];
                 """,
                null,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision SQL Server database/login for {Identifier}; attempting cleanup", identifier);
            await CleanupSqlServerDatabaseAndLoginAsync(identifier, loginName, !hasExistingDatabases, CancellationToken.None);
            throw;
        }
    }

    private async Task EnsureSharedLoginAsync(
        string loginName,
        string password,
        bool loginExists,
        bool hasExistingDatabases,
        CancellationToken cancellationToken)
    {
        if (loginExists && hasExistingDatabases)
        {
            return;
        }

        var commandText = loginExists
            ? $"ALTER LOGIN [{loginName}] WITH PASSWORD = @Password, CHECK_POLICY = ON, CHECK_EXPIRATION = OFF"
            : $"CREATE LOGIN [{loginName}] WITH PASSWORD = @Password, CHECK_POLICY = ON, CHECK_EXPIRATION = OFF";

        await _sqlServerExecutor.ExecuteNonQueryAsync(
            commandText,
            command => command.AddParameter("@Password", password),
            cancellationToken);
    }

    private async Task CleanupSqlServerDatabaseAndLoginAsync(
        string identifier,
        string loginName,
        bool dropLogin,
        CancellationToken cancellationToken)
    {
        try
        {
            await _sqlServerExecutor.ExecuteNonQueryAsync(
                $"""
                 IF DB_ID(N'{identifier}') IS NOT NULL
                 BEGIN
                     ALTER DATABASE [{identifier}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                     DROP DATABASE [{identifier}];
                 END
                 """,
                null,
                cancellationToken);

            if (dropLogin)
            {
                await _sqlServerExecutor.ExecuteNonQueryAsync(
                    $"""
                     IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{loginName}')
                     BEGIN
                         DROP LOGIN [{loginName}];
                     END
                     """,
                    null,
                    cancellationToken);
            }
        }
        catch (Exception cleanupEx)
        {
            _logger.LogError(cleanupEx, "Best-effort SQL Server cleanup failed for identifier {Identifier}; manual cleanup required", identifier);
        }
    }

    private async Task SetDatabaseConnectPermissionAsync(
        string databaseName,
        string loginName,
        bool allowConnect,
        CancellationToken cancellationToken)
    {
        var commandText = allowConnect
            ? $"USE [{databaseName}]; REVOKE CONNECT FROM [{loginName}];"
            : $"USE [{databaseName}]; DENY CONNECT TO [{loginName}];";

        await _sqlServerExecutor.ExecuteNonQueryAsync(commandText, null, cancellationToken);
    }

    private async Task<bool> LoginExistsAsync(string loginName, CancellationToken cancellationToken)
    {
        var results = await _sqlServerExecutor.QueryAsync(
            """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM sys.server_principals
                WHERE name = @LoginName
            ) THEN 1 ELSE 0 END AS bit)
            """,
            command => command.AddParameter("@LoginName", loginName),
            reader => reader.GetBoolean(0),
            cancellationToken);

        return results.Single();
    }

    private async Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken cancellationToken)
    {
        var results = await _sqlServerExecutor.QueryAsync(
            """
            SELECT CAST(CASE WHEN DB_ID(@DatabaseName) IS NULL THEN 0 ELSE 1 END AS bit)
            """,
            command => command.AddParameter("@DatabaseName", databaseName),
            reader => reader.GetBoolean(0),
            cancellationToken);

        return results.Single();
    }

    private async Task<SqlServerSharedProvisioningStateDto> GetSharedProvisioningStateAsync(int userId, CancellationToken cancellationToken)
    {
        var state = await _sqlExecutor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_GetSharedSqlServerProvisioningState,
            command => command.AddParameter("@UserId", userId),
            reader => new SqlServerSharedProvisioningStateDto
            {
                SharedLoginName = reader.GetStringOrEmpty("SharedLoginName"),
                HasExistingDatabases = reader.GetBooleanValue("HasExistingDatabases"),
                EncryptedPassword = reader.GetNullableString("EncryptedPassword")
            },
            cancellationToken);

        return state ?? throw new InvalidOperationException($"Failed to resolve shared SQL Server provisioning state for user {userId}.");
    }

    private async Task<SqlServerSharedLoginCleanupStateDto> GetSharedLoginCleanupStateAsync(int userId, CancellationToken cancellationToken)
    {
        var state = await _sqlExecutor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.DatabaseInstances_GetSharedLoginCleanupState,
            command => command.AddParameter("@UserId", userId),
            reader => new SqlServerSharedLoginCleanupStateDto
            {
                SharedLoginName = reader.GetStringOrEmpty("SharedLoginName"),
                CanDropLogin = reader.GetBooleanValue("CanDropLogin")
            },
            cancellationToken);

        return state ?? throw new InvalidOperationException($"Failed to resolve shared SQL Server cleanup state for user {userId}.");
    }

    private static void ValidateIdentifier(string identifier)
    {
        if (!IdentifierRegex().IsMatch(identifier))
        {
            throw new InvalidOperationException($"Generated SQL Server identifier '{identifier}' failed validation.");
        }
    }

    [GeneratedRegex(@"^[a-z0-9_]{1,32}$")]
    private static partial Regex IdentifierRegex();
}
