using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MySqlConnector;
using raft_backend.Configuration;
using raft_backend.DTOs;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public class MySqlProvisioningService : IEngineProvisioningService
{
    public string EngineName => "MySQL";

    private readonly IConfiguration _configuration;
    private readonly ISecurePasswordGenerator _passwordGenerator;
    private readonly IDatabaseInstanceService _databaseInstanceService;
    private readonly IAccessCredentialService _accessCredentialService;
    private readonly IAuditEventService _auditEventService;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly MySqlProvisioningOptions _options;
    private readonly ILogger<MySqlProvisioningService> _logger;

    public MySqlProvisioningService(
        IConfiguration configuration,
        ISecurePasswordGenerator passwordGenerator,
        IDatabaseInstanceService databaseInstanceService,
        IAccessCredentialService accessCredentialService,
        IAuditEventService auditEventService,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<MySqlProvisioningOptions> options,
        ILogger<MySqlProvisioningService> logger)
    {
        _configuration = configuration;
        _passwordGenerator = passwordGenerator;
        _databaseInstanceService = databaseInstanceService;
        _accessCredentialService = accessCredentialService;
        _auditEventService = auditEventService;
        _dataProtectionProvider = dataProtectionProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SqlServerProvisioningResultDto> ProvisionAsync(int userId, CancellationToken cancellationToken = default)
    {
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var identifier = $"raft_my_u{userId}_{suffix}";
        var password = _passwordGenerator.Generate(_options.PasswordLength);

        var connString = _configuration.GetConnectionString("MySqlProvisioning")
            ?? throw new InvalidOperationException("MySqlProvisioning connection string is missing.");

        // Invocación al procedimiento almacenado nativo MySQL sp_create_database_and_user
        await using (var conn = new MySqlConnection(connString))
        {
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CALL sp_create_database_and_user(@p_dbname, @p_dbuser, @p_dbpass);";
            cmd.Parameters.AddWithValue("@p_dbname", identifier);
            cmd.Parameters.AddWithValue("@p_dbuser", identifier);
            cmd.Parameters.AddWithValue("@p_dbpass", password);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Cifrado y registro de la metadata en SQL Server (RaftDb)
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
            LastActivity = DateTime.UtcNow
        }, cancellationToken) ?? throw new InvalidOperationException("Failed to persist MySQL database instance.");

        await _accessCredentialService.CreateAsync(new AccessCredentialCreateDto
        {
            DatabaseInstanceId = instance.Id,
            EncryptedPassword = encryptedPassword
        }, cancellationToken);

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = userId,
            EventType = "Provisioning",
            Description = $"MySQL database '{identifier}' provisioned successfully."
        }, cancellationToken);

        return new SqlServerProvisioningResultDto
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
}
