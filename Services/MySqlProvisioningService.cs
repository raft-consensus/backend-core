using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using raft_backend.Configuration;
using raft_backend.Database;
using raft_backend.DTOs;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public partial class MySqlProvisioningService : IDatabaseProvisioningService
{
    private readonly IUserDashboardService _dashboardService;
    private readonly IAccessCredentialService _accessCredentialService;
    private readonly IAuditEventService _auditEventService;
    private readonly IDatabaseInstanceService _databaseInstanceService;
    private readonly ISecurePasswordGenerator _passwordGenerator;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly MySqlProvisioningOptions _options;
    private readonly ExternalCellConnectionStrings _connectionStrings;
    private readonly ILogger<MySqlProvisioningService> _logger;

    public MySqlProvisioningService(
        IUserDashboardService dashboardService,
        IAccessCredentialService accessCredentialService,
        IAuditEventService auditEventService,
        IDatabaseInstanceService databaseInstanceService,
        ISecurePasswordGenerator passwordGenerator,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<MySqlProvisioningOptions> options,
        IOptions<ExternalCellConnectionStrings> connectionStrings,
        ILogger<MySqlProvisioningService> logger)
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

    public string Engine => "MySQL";

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_connectionStrings.MySqlProvisioning);

    public int MaxDatabasesPerUser => _options.MaxDatabasesPerUser;

    public async Task<DatabaseProvisioningResultDto> ProvisionDatabaseAsync(int userId, CancellationToken cancellationToken = default)
    {
        var sharedState = await GetSharedProvisioningStateAsync(userId, cancellationToken);
        var protector = _dataProtectionProvider.CreateProtector(DataProtectionPurposes.AccessCredentialPassword);
        var sharedLoginName = sharedState.SharedLoginName;
        string password;
        if (sharedState.EncryptedPassword is null)
        {
            password = _passwordGenerator.Generate(_options.PasswordLength);
        }
        else
        {
            try
            {
                password = protector.Unprotect(sharedState.EncryptedPassword);
            }
            catch (CryptographicException)
            {
                password = _passwordGenerator.Generate(_options.PasswordLength);
            }
        }

        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var identifier = $"raft_u{userId}_{suffix}";
        ValidateIdentifier(identifier);

        try
        {
            await CreateDatabaseAndUserAsync(identifier, sharedLoginName, password, cancellationToken);

            var encryptedPassword = protector.Protect(password);
            var instance = await _databaseInstanceService.CreateAsync(new DatabaseInstanceCreateDto
            {
                UserId = userId,
                Host = _options.PublicHost,
                Port = _options.PublicPort,
                DatabaseName = identifier,
                DatabaseUser = sharedLoginName,
                Engine = Engine,
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
                Description = $"MySQL database '{identifier}' provisioned through self-service."
            }, cancellationToken);

            return new MySqlProvisioningResultDto
            {
                DatabaseInstanceId = instance.Id,
                Host = _options.PublicHost,
                Port = _options.PublicPort,
                DatabaseName = identifier,
                DatabaseUser = sharedLoginName,
                Password = password,
                Engine = Engine
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision MySQL database for {Identifier}", identifier);
            await SafeDeleteDatabaseAndUserAsync(identifier, sharedLoginName, userId, CancellationToken.None);
            throw;
        }
    }

    public async Task PauseAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        ValidateIdentifier(instance.DatabaseName);
        ValidateIdentifier(instance.DatabaseUser);
        await KillSessionsAsync(instance.DatabaseName, instance.DatabaseUser, cancellationToken);
        await ExecuteAsync(
            $"""REVOKE ALL PRIVILEGES, GRANT OPTION ON `{instance.DatabaseName}`.* FROM '{instance.DatabaseUser}'@'%';""",
            null,
            cancellationToken);
        await ExecuteAsync("FLUSH PRIVILEGES;", null, cancellationToken);
        await UpdateStatusAsync(databaseInstanceId, "Suspended", cancellationToken);
    }

    public async Task ResumeAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        ValidateIdentifier(instance.DatabaseName);
        ValidateIdentifier(instance.DatabaseUser);
        await ExecuteAsync(
            $"""GRANT ALL PRIVILEGES ON `{instance.DatabaseName}`.* TO '{instance.DatabaseUser}'@'%';""",
            null,
            cancellationToken);
        await ExecuteAsync("FLUSH PRIVILEGES;", null, cancellationToken);
        await UpdateStatusAsync(databaseInstanceId, "Active", cancellationToken);
    }

    public async Task DeleteAsync(int databaseInstanceId, CancellationToken cancellationToken = default)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

        var owned = await _dashboardService.GetByUserIdAsync(instance.UserId, cancellationToken);
        var isLastDatabase = owned.Count <= 1;

        ValidateIdentifier(instance.DatabaseName);
        ValidateIdentifier(instance.DatabaseUser);

        await ExecuteAsync($"""DROP DATABASE IF EXISTS `{instance.DatabaseName}`;""", null, cancellationToken);

        if (isLastDatabase)
        {
            await ExecuteAsync($"""DROP USER IF EXISTS '{instance.DatabaseUser}'@'%';""", null, cancellationToken);
        }

        await _databaseInstanceService.SoftDeleteAsync(databaseInstanceId, cancellationToken);
    }

    public async Task<long> GetUsedSpaceBytesAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(databaseName);

        var results = await QueryAsync(
            """
            SELECT COALESCE(SUM(data_length + index_length), 0) AS UsedBytes
            FROM information_schema.tables
            WHERE table_schema = @DatabaseName
            """,
            command => command.AddParameter("@DatabaseName", databaseName),
            reader => reader.GetInt64Value("UsedBytes"),
            cancellationToken);

        return results.SingleOrDefault();
    }

    public async Task<int> GetActiveConnectionCountAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(databaseName);

        var results = await QueryAsync(
            """
            SELECT COUNT(*) AS ActiveConnections
            FROM information_schema.processlist
            WHERE db = @DatabaseName
              AND command <> 'Sleep'
            """,
            command => command.AddParameter("@DatabaseName", databaseName),
            reader => reader.GetInt64Value("ActiveConnections"),
            cancellationToken);

        var count = results.SingleOrDefault();
        return count > int.MaxValue ? int.MaxValue : (int)count;
    }

    private async Task<(string SharedLoginName, string? EncryptedPassword)> GetSharedProvisioningStateAsync(int userId, CancellationToken cancellationToken)
    {
        var databases = await _dashboardService.GetByUserIdAsync(userId, cancellationToken);
        var sharedLoginName = $"raft_u{userId}";

        if (databases.Count == 0)
        {
            return (sharedLoginName, null);
        }

        var firstCredential = await _accessCredentialService.GetByDatabaseInstanceIdAsync(databases[0].DatabaseInstanceId, cancellationToken);
        if (firstCredential is null)
        {
            return (sharedLoginName, null);
        }

        var encryptor = _dataProtectionProvider.CreateProtector(DataProtectionPurposes.AccessCredentialPassword);
        var instanceReveal = await _accessCredentialService.RevealPasswordAsync(userId, databases[0].DatabaseInstanceId, cancellationToken);
        return (sharedLoginName, instanceReveal is null ? null : encryptor.Protect(instanceReveal.Password));
    }

    private async Task CreateDatabaseAndUserAsync(
        string databaseName,
        string loginName,
        string password,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync($"""CREATE DATABASE IF NOT EXISTS `{databaseName}`;""", null, cancellationToken);
        await ExecuteAsync(
            $"""CREATE USER IF NOT EXISTS '{loginName}'@'%' IDENTIFIED BY '{password}';""",
            null,
            cancellationToken);
        await ExecuteAsync(
            $"""ALTER USER '{loginName}'@'%' IDENTIFIED BY '{password}';""",
            null,
            cancellationToken);
        await ExecuteAsync(
            $"""GRANT ALL PRIVILEGES ON `{databaseName}`.* TO '{loginName}'@'%';""",
            null,
            cancellationToken);
        await ExecuteAsync("FLUSH PRIVILEGES;", null, cancellationToken);
    }

    private async Task SafeDeleteDatabaseAndUserAsync(string databaseName, string loginName, int userId, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync($"""DROP DATABASE IF EXISTS `{databaseName}`;""", null, cancellationToken);

            var remaining = await _dashboardService.GetByUserIdAsync(userId, cancellationToken);
            if (remaining.Count == 0)
            {
                await ExecuteAsync($"""DROP USER IF EXISTS '{loginName}'@'%';""", null, cancellationToken);
            }
        }
        catch (Exception cleanupEx)
        {
            _logger.LogError(cleanupEx, "Best-effort MySQL cleanup failed for database {DatabaseName}", databaseName);
        }
    }

    private async Task KillSessionsAsync(string databaseName, string loginName, CancellationToken cancellationToken)
    {
        var sessionIds = await QueryAsync(
            """
            SELECT ID
            FROM information_schema.processlist
            WHERE db = @DatabaseName
              AND user = @LoginName
            """,
            command =>
            {
                command.AddParameter("@DatabaseName", databaseName);
                command.AddParameter("@LoginName", loginName);
            },
            reader => reader.GetInt32Value("ID"),
            cancellationToken);

        foreach (var sessionId in sessionIds.Distinct())
        {
            await ExecuteAsync($"KILL {sessionId};", null, cancellationToken);
        }
    }

    private async Task UpdateStatusAsync(int databaseInstanceId, string status, CancellationToken cancellationToken)
    {
        var instance = await _databaseInstanceService.GetByIdAsync(databaseInstanceId, cancellationToken)
            ?? throw new InvalidOperationException($"Database instance {databaseInstanceId} not found.");

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

    private async Task<int> ExecuteAsync(string commandText, Action<DbCommand>? configureCommand, CancellationToken cancellationToken, string? connectionString = null)
    {
        var connString = connectionString ?? GetConnectionString();
        await using var connection = new MySqlConnection(connString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        configureCommand?.Invoke(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<List<T>> QueryAsync<T>(string commandText, Action<DbCommand>? configureCommand, Func<DbDataReader, T> map, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        configureCommand?.Invoke(command);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(map(reader));
        }

        return items;
    }

    private string GetConnectionString()
    {
        return !string.IsNullOrWhiteSpace(_connectionStrings.MySqlProvisioning)
            ? _connectionStrings.MySqlProvisioning
            : throw new InvalidOperationException("Missing connection string: ConnectionStrings:MySqlProvisioning");
    }

    private static void ValidateIdentifier(string identifier)
    {
        if (!IdentifierRegex().IsMatch(identifier))
        {
            throw new InvalidOperationException($"MySQL identifier '{identifier}' failed validation.");
        }
    }

    [GeneratedRegex(@"^[a-z0-9_]{1,64}$")]
    private static partial Regex IdentifierRegex();
}
