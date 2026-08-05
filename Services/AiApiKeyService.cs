using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using raft_backend.Database;
using raft_backend.DTOs;

namespace raft_backend.Services;

public class AiApiKeyService : IAiApiKeyService
{
    private readonly ISqlStoredProcedureExecutor _executor;
    private readonly IAuditEventService _auditEventService;
    private readonly ILogger<AiApiKeyService> _logger;

    public AiApiKeyService(
        ISqlStoredProcedureExecutor executor,
        IAuditEventService auditEventService,
        ILogger<AiApiKeyService> logger)
    {
        _executor = executor;
        _auditEventService = auditEventService;
        _logger = logger;
    }

    public Task<IReadOnlyList<AiApiKeyReadDto>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _executor.QueryAsync(
            StoredProcedureNames.AiApiKeys_GetAllByUserId,
            command => command.AddParameter("@UserId", userId),
            MapRead,
            cancellationToken).ContinueWith(static task => (IReadOnlyList<AiApiKeyReadDto>)task.Result, cancellationToken);
    }

    public Task<AiApiKeyReadDto?> GetByIdAsync(int userId, int id, CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.AiApiKeys_GetByIdAndUserId,
            command =>
            {
                command.AddParameter("@Id", id);
                command.AddParameter("@UserId", userId);
            },
            MapRead,
            cancellationToken);
    }

    public async Task<AiApiKeySecretDto?> CreateAsync(int userId, AiApiKeyCreateDto dto, CancellationToken cancellationToken = default)
    {
        var secret = GenerateSecret();
        var keyPrefix = secret[..Math.Min(8, secret.Length)];
        var keyHash = HashSecret(secret);

        var item = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.AiApiKeys_Create,
            command =>
            {
                command.AddParameter("@UserId", userId);
                command.AddParameter("@Name", dto.Name);
                command.AddParameter("@KeyPrefix", keyPrefix);
                command.AddParameter("@KeyHash", keyHash);
            },
            MapRead,
            cancellationToken);

        if (item is null)
        {
            return null;
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = userId,
            EventType = "AiApiKeyCreated",
            Description = $"AI API key '{item.Name}' was created.",
            AdditionalData = $"{{\"aiApiKeyId\":{item.Id},\"keyPrefix\":\"{item.KeyPrefix}\"}}"
        }, cancellationToken);

        return new AiApiKeySecretDto
        {
            Key = item,
            Secret = secret
        };
    }

    public async Task<AiApiKeySecretDto?> RotateAsync(int userId, int id, CancellationToken cancellationToken = default)
    {
        var current = await GetByIdAsync(userId, id, cancellationToken);
        if (current is null)
        {
            return null;
        }

        var secret = GenerateSecret();
        var keyPrefix = secret[..Math.Min(8, secret.Length)];
        var keyHash = HashSecret(secret);

        var item = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.AiApiKeys_Rotate,
            command =>
            {
                command.AddParameter("@Id", id);
                command.AddParameter("@UserId", userId);
                command.AddParameter("@KeyPrefix", keyPrefix);
                command.AddParameter("@KeyHash", keyHash);
            },
            MapRead,
            cancellationToken);

        if (item is null)
        {
            return null;
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = userId,
            EventType = "AiApiKeyRotated",
            Description = $"AI API key {id} was rotated.",
            AdditionalData = $"{{\"aiApiKeyId\":{item.Id},\"keyPrefix\":\"{item.KeyPrefix}\"}}"
        }, cancellationToken);

        return new AiApiKeySecretDto
        {
            Key = item,
            Secret = secret
        };
    }

    public async Task<bool> RevokeAsync(int userId, int id, CancellationToken cancellationToken = default)
    {
        var rows = await _executor.ExecuteAsync(
            StoredProcedureNames.AiApiKeys_Revoke,
            command =>
            {
                command.AddParameter("@Id", id);
                command.AddParameter("@UserId", userId);
            },
            cancellationToken);

        if (rows <= 0)
        {
            return false;
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = userId,
            EventType = "AiApiKeyRevoked",
            Description = $"AI API key {id} was revoked."
        }, cancellationToken);

        return true;
    }

    public Task<AiApiKeyReadDto?> ResolveBySecretAsync(string apiKeySecret, CancellationToken cancellationToken = default)
    {
        var keyHash = HashSecret(apiKeySecret);
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.AiApiKeys_GetActiveByKeyHash,
            command => command.AddParameter("@KeyHash", keyHash),
            MapRead,
            cancellationToken);
    }

    public async Task<bool> RecordUsageAsync(int keyId, long promptTokens, long completionTokens, decimal approxCostUsd, CancellationToken cancellationToken = default)
    {
        var rows = await _executor.ExecuteAsync(
            StoredProcedureNames.AiApiKeys_RecordUsage,
            command =>
            {
                command.AddParameter("@Id", keyId);
                command.AddParameter("@PromptTokens", promptTokens);
                command.AddParameter("@CompletionTokens", completionTokens);
                command.AddParameter("@ApproxCostUsd", approxCostUsd);
            },
            cancellationToken);

        return rows > 0;
    }

    private static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    private static string HashSecret(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static AiApiKeyReadDto MapRead(DbDataReader reader)
    {
        return new AiApiKeyReadDto
        {
            Id = reader.GetInt32Value("Id"),
            UserId = reader.GetInt32Value("UserId"),
            Name = reader.GetStringOrEmpty("Name"),
            KeyPrefix = reader.GetStringOrEmpty("KeyPrefix"),
            Status = reader.GetStringOrEmpty("Status"),
            CreatedAt = reader.GetDateTimeValue("CreatedAt"),
            UpdatedAt = reader.GetNullableDateTime("UpdatedAt"),
            RevokedAt = reader.GetNullableDateTime("RevokedAt"),
            LastUsedAt = reader.GetNullableDateTime("LastUsedAt"),
            TotalRequests = reader.GetInt64Value("TotalRequests"),
            TotalPromptTokens = reader.GetInt64Value("TotalPromptTokens"),
            TotalCompletionTokens = reader.GetInt64Value("TotalCompletionTokens"),
            TotalTokens = reader.GetInt64Value("TotalTokens"),
            ApproxCostUsd = reader.GetDecimalValue("ApproxCostUsd")
        };
    }
}
