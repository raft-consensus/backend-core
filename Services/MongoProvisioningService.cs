using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using raft_backend.Configuration;
using raft_backend.DTOs;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public class MongoProvisioningService : IEngineProvisioningService
{
    public string EngineName => "MongoDB";

    private readonly IConfiguration _configuration;
    private readonly ISecurePasswordGenerator _passwordGenerator;
    private readonly IDatabaseInstanceService _databaseInstanceService;
    private readonly IAccessCredentialService _accessCredentialService;
    private readonly IAuditEventService _auditEventService;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly MongoProvisioningOptions _options;
    private readonly ILogger<MongoProvisioningService> _logger;

    public MongoProvisioningService(
        IConfiguration configuration,
        ISecurePasswordGenerator passwordGenerator,
        IDatabaseInstanceService databaseInstanceService,
        IAccessCredentialService accessCredentialService,
        IAuditEventService auditEventService,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<MongoProvisioningOptions> options,
        ILogger<MongoProvisioningService> logger)
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
        var identifier = $"raft_mg_u{userId}_{suffix}";
        var password = _passwordGenerator.Generate(_options.PasswordLength);

        var connString = _configuration.GetConnectionString("MongoProvisioning")
            ?? throw new InvalidOperationException("MongoProvisioning connection string is missing.");

        // Creación del usuario y asignación de rol sobre la base de datos de MongoDB
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
            Engine = "MongoDB",
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

        return new SqlServerProvisioningResultDto
        {
            DatabaseInstanceId = instance.Id,
            Host = _options.PublicHost,
            Port = _options.PublicPort,
            DatabaseName = identifier,
            DatabaseUser = identifier,
            Password = password,
            Engine = "MongoDB"
        };
    }
}
