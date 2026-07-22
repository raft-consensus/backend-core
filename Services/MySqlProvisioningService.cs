using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.Database;
using raft_backend.DTOs;

namespace raft_backend.Services;

// Mechanical execution only: which user gets provisioned and when is decided by the caller
// (AuthService, on IsNewUser). This service just talks to the real MySQL server and persists
// the result through the already-SP-backed IDatabaseInstanceService/IAccessCredentialService.
public partial class MySqlProvisioningService : IMySqlProvisioningService
{
    private readonly IMySqlCommandExecutor _mySqlExecutor;
    private readonly ISqlStoredProcedureExecutor _sqlExecutor;
    private readonly ISecurePasswordGenerator _passwordGenerator;
    private readonly IDatabaseInstanceService _databaseInstanceService;
    private readonly IAccessCredentialService _accessCredentialService;
    private readonly IAuditEventService _auditEventService;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly MySqlProvisioningOptions _options;
    private readonly ILogger<MySqlProvisioningService> _logger;

    public MySqlProvisioningService(
        IMySqlCommandExecutor mySqlExecutor,
        ISqlStoredProcedureExecutor sqlExecutor,
        ISecurePasswordGenerator passwordGenerator,
        IDatabaseInstanceService databaseInstanceService,
        IAccessCredentialService accessCredentialService,
        IAuditEventService auditEventService,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<MySqlProvisioningOptions> options,
        ILogger<MySqlProvisioningService> logger)
    {
        _mySqlExecutor = mySqlExecutor;
        _sqlExecutor = sqlExecutor;
        _passwordGenerator = passwordGenerator;
        _databaseInstanceService = databaseInstanceService;
        _accessCredentialService = accessCredentialService;
        _auditEventService = auditEventService;
        _dataProtectionProvider = dataProtectionProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MySqlProvisioningResultDto> ProvisionDatabaseAsync(int userId, CancellationToken cancellationToken = default)
    {
        // Identifier is always synthesized server-side, never derived from request/user
        // input — this is what makes interpolating it into MySQL DDL safe despite MySQL
        // not supporting parameterized identifiers (CREATE DATABASE ? is not valid syntax).
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var identifier = $"raft_u{userId}_{suffix}";
        ValidateIdentifier(identifier);

        var password = _passwordGenerator.Generate(_options.PasswordLength);

        await CreateMySqlDatabaseAndUserAsync(identifier, password, cancellationToken);

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
                Engine = "MySQL",
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
                Description = $"MySQL database '{identifier}' provisioned automatically after first login."
            }, cancellationToken);

            return new MySqlProvisioningResultDto
            {
                DatabaseInstanceId = instance.Id,
                Host = _options.PublicHost,
                Port = _options.PublicPort,
                DatabaseName = identifier,
                DatabaseUser = identifier,
                Password = password,
                Engine = "MySQL"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist provisioning result for {Identifier}; rolling back MySQL side", identifier);
            await CleanupMySqlDatabaseAndUserAsync(identifier);
            throw;
        }
    }

    public async Task PauseAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        await _mySqlExecutor.ExecuteNonQueryAsync($"ALTER USER '{instance.DatabaseUser}'@'%' ACCOUNT LOCK", null, cancellationToken);
        await UpdateStatusAsync(databaseInstanceId, "Suspended", cancellationToken);
    }

    public async Task ResumeAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        await _mySqlExecutor.ExecuteNonQueryAsync($"ALTER USER '{instance.DatabaseUser}'@'%' ACCOUNT UNLOCK", null, cancellationToken);
        await UpdateStatusAsync(databaseInstanceId, "Active", cancellationToken);
    }

    public async Task DeleteAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        await _mySqlExecutor.ExecuteNonQueryAsync($"DROP DATABASE IF EXISTS `{instance.DatabaseName}`", null, cancellationToken);
        await _mySqlExecutor.ExecuteNonQueryAsync($"DROP USER IF EXISTS '{instance.DatabaseUser}'@'%'", null, cancellationToken);

        // Also soft-deletes the associated AccessCredentials row (see usp_DatabaseInstances_SoftDelete) —
        // EF's cascade convention only fires on EF-driven hard deletes, not SP-driven soft deletes.
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

    private async Task CreateMySqlDatabaseAndUserAsync(string identifier, string password, CancellationToken cancellationToken)
    {
        try
        {
            await _mySqlExecutor.ExecuteNonQueryAsync($"CREATE DATABASE `{identifier}`", null, cancellationToken);

            await _mySqlExecutor.ExecuteNonQueryAsync(
                $"CREATE USER '{identifier}'@'%' IDENTIFIED BY @Password",
                command => command.AddParameter("@Password", password),
                cancellationToken);

            await _mySqlExecutor.ExecuteNonQueryAsync(
                $"GRANT ALL PRIVILEGES ON `{identifier}`.* TO '{identifier}'@'%'",
                null,
                cancellationToken);

            await _mySqlExecutor.ExecuteNonQueryAsync(
                $"ALTER USER '{identifier}'@'%' WITH MAX_USER_CONNECTIONS {_options.DefaultMaxUserConnections}",
                null,
                cancellationToken);

            await _mySqlExecutor.ExecuteNonQueryAsync("FLUSH PRIVILEGES", null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision MySQL database/user for {Identifier}; attempting cleanup", identifier);
            await CleanupMySqlDatabaseAndUserAsync(identifier);
            throw;
        }
    }

    private async Task CleanupMySqlDatabaseAndUserAsync(string identifier)
    {
        try
        {
            await _mySqlExecutor.ExecuteNonQueryAsync($"DROP DATABASE IF EXISTS `{identifier}`", null, CancellationToken.None);
            await _mySqlExecutor.ExecuteNonQueryAsync($"DROP USER IF EXISTS '{identifier}'@'%'", null, CancellationToken.None);
        }
        catch (Exception cleanupEx)
        {
            _logger.LogError(cleanupEx, "Best-effort MySQL cleanup failed for identifier {Identifier}; manual cleanup required", identifier);
        }
    }

    private static void ValidateIdentifier(string identifier)
    {
        if (!IdentifierRegex().IsMatch(identifier))
        {
            throw new InvalidOperationException($"Generated MySQL identifier '{identifier}' failed validation.");
        }
    }

    [GeneratedRegex(@"^[a-z0-9_]{1,32}$")]
    private static partial Regex IdentifierRegex();
}
