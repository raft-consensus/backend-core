using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.Database;
using raft_backend.DTOs;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public partial class SqlServerProvisioningService : ISqlServerProvisioningService, IEngineProvisioningService
{
    public string EngineName => "SQL Server";

    public Task<SqlServerProvisioningResultDto> ProvisionAsync(int userId, CancellationToken cancellationToken = default)
        => ProvisionDatabaseAsync(userId, cancellationToken);
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
        for (var attempt = 1; attempt <= MaxProvisioningAttempts; attempt++)
        {
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
            var identifier = $"raft_u{userId}_{suffix}";
            ValidateIdentifier(identifier);

            var password = _passwordGenerator.Generate(_options.PasswordLength);

            try
            {
                await CreateSqlServerDatabaseAndLoginAsync(identifier, password, cancellationToken);
            }
            catch (Exception ex) when (attempt < MaxProvisioningAttempts)
            {
                _logger.LogWarning(ex, "SQL Server provisioning attempt {Attempt} failed for {Identifier}; retrying with a new identifier", attempt, identifier);
                continue;
            }

            try
            {
                var protector = _dataProtectionProvider.CreateProtector(DataProtectionPurposes.AccessCredentialPassword);
                var encryptedPassword = protector.Protect(password);

                var instance = await _databaseInstanceService.CreateAsync(new DatabaseInstanceCreateDto
                {
                    UserId = userId,
                    Host = _options.PublicHost,
                    Port = _options.PublicPort,
                    DatabaseName = identifier,
                    DatabaseUser = identifier,
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
                    Description = $"SQL Server database '{identifier}' provisioned automatically after first login."
                }, cancellationToken);

                return new SqlServerProvisioningResultDto
                {
                    DatabaseInstanceId = instance.Id,
                    Host = _options.PublicHost,
                    Port = _options.PublicPort,
                    DatabaseName = identifier,
                    DatabaseUser = identifier,
                    Password = password,
                    Engine = "SQL Server"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist provisioning result for {Identifier}; rolling back SQL Server side", identifier);
                await CleanupSqlServerDatabaseAndLoginAsync(identifier);
                throw;
            }
        }

        throw new InvalidOperationException("SQL Server database provisioning failed after multiple attempts.");
    }

    public async Task PauseAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        await _sqlServerExecutor.ExecuteNonQueryAsync($"ALTER LOGIN [{instance.DatabaseUser}] DISABLE", null, cancellationToken);
        await UpdateStatusAsync(databaseInstanceId, "Suspended", cancellationToken);
    }

    public async Task ResumeAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        await _sqlServerExecutor.ExecuteNonQueryAsync($"ALTER LOGIN [{instance.DatabaseUser}] ENABLE", null, cancellationToken);
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

        await _sqlServerExecutor.ExecuteNonQueryAsync(
            $"""
             IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{instance.DatabaseUser}')
             BEGIN
                 DROP LOGIN [{instance.DatabaseUser}];
             END
             """,
            null,
            cancellationToken);

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

    private async Task CreateSqlServerDatabaseAndLoginAsync(string identifier, string password, CancellationToken cancellationToken)
    {
        try
        {
            await _sqlServerExecutor.ExecuteNonQueryAsync($"CREATE DATABASE [{identifier}]", null, cancellationToken);

            await _sqlServerExecutor.ExecuteNonQueryAsync(
                $"CREATE LOGIN [{identifier}] WITH PASSWORD = @Password, CHECK_POLICY = ON, CHECK_EXPIRATION = OFF",
                command => command.AddParameter("@Password", password),
                cancellationToken);

            await _sqlServerExecutor.ExecuteNonQueryAsync(
                $"""
                 USE [{identifier}];
                 CREATE USER [{identifier}] FOR LOGIN [{identifier}];
                 ALTER ROLE [db_owner] ADD MEMBER [{identifier}];
                 """,
                null,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision SQL Server database/login for {Identifier}; attempting cleanup", identifier);
            await CleanupSqlServerDatabaseAndLoginAsync(identifier);
            throw;
        }
    }

    private async Task CleanupSqlServerDatabaseAndLoginAsync(string identifier)
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
                CancellationToken.None);

            await _sqlServerExecutor.ExecuteNonQueryAsync(
                $"""
                 IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{identifier}')
                 BEGIN
                     DROP LOGIN [{identifier}];
                 END
                 """,
                null,
                CancellationToken.None);
        }
        catch (Exception cleanupEx)
        {
            _logger.LogError(cleanupEx, "Best-effort SQL Server cleanup failed for identifier {Identifier}; manual cleanup required", identifier);
        }
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
