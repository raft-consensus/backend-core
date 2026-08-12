using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using raft_backend.Database;
using raft_backend.DTOs;

namespace raft_backend.Services;

public class AiApiKeyService : IAiApiKeyService
{
    private readonly ISqlStoredProcedureExecutor _executor;
    private readonly RaftDbContext _context;
    private readonly IAuditEventService _auditEventService;
    private readonly ILogger<AiApiKeyService> _logger;

    public AiApiKeyService(
        ISqlStoredProcedureExecutor executor,
        RaftDbContext context,
        IAuditEventService auditEventService,
        ILogger<AiApiKeyService> logger)
    {
        _executor = executor;
        _context = context;
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

    public async Task<bool> RecordUsageAsync(
        int keyId,
        int? userId,
        string provider,
        string model,
        string endpoint,
        string? mode,
        long promptTokens,
        long completionTokens,
        decimal approxCostUsd,
        int? durationMs = null,
        int statusCode = 200,
        CancellationToken cancellationToken = default)
    {
        var rows = await _executor.ExecuteAsync(
            StoredProcedureNames.AiApiKeys_RecordUsage,
            command =>
            {
                command.AddParameter("@Id", keyId);
                command.AddParameter("@UserId", userId.HasValue && userId.Value > 0 ? userId.Value : DBNull.Value);
                command.AddParameter("@Provider", provider);
                command.AddParameter("@Model", model);
                command.AddParameter("@Endpoint", endpoint);
                command.AddParameter("@Mode", string.IsNullOrWhiteSpace(mode) ? DBNull.Value : mode);
                command.AddParameter("@PromptTokens", promptTokens);
                command.AddParameter("@CompletionTokens", completionTokens);
                command.AddParameter("@ApproxCostUsd", approxCostUsd);
                command.AddParameter("@DurationMs", durationMs.HasValue ? durationMs.Value : DBNull.Value);
                command.AddParameter("@StatusCode", statusCode);
            },
            cancellationToken);

        return rows > 0;
    }

    public async Task<IReadOnlyList<AiUsageLogReadDto>> GetUsageHistoryAsync(
        int userId,
        AiUsageHistoryFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        return await _executor.QueryAsync(
            StoredProcedureNames.AiUsageLogs_GetHistoryByUserId,
            command =>
            {
                command.AddParameter("@UserId", userId);
                command.AddParameter("@AiApiKeyId", filter.AiApiKeyId.HasValue && filter.AiApiKeyId.Value > 0 ? filter.AiApiKeyId.Value : DBNull.Value);
                command.AddParameter("@FromDate", filter.FromDate.HasValue ? filter.FromDate.Value : DBNull.Value);
                command.AddParameter("@ToDate", filter.ToDate.HasValue ? filter.ToDate.Value : DBNull.Value);
                command.AddParameter("@PageNumber", filter.PageNumber > 0 ? filter.PageNumber : 1);
                command.AddParameter("@PageSize", filter.PageSize > 0 ? filter.PageSize : 50);
            },
            MapUsageLogRead,
            cancellationToken);
    }

    public async Task<AiUsageAnalyticsDto> GetUsageAnalyticsAsync(
        int userId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var analytics = new AiUsageAnalyticsDto();
        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken);
            }

            using var command = connection.CreateCommand();
            command.CommandText = StoredProcedureNames.AiUsageLogs_GetAnalyticsByUserId;
            command.CommandType = System.Data.CommandType.StoredProcedure;
            command.AddParameter("@UserId", userId);
            command.AddParameter("@FromDate", fromDate.HasValue ? fromDate.Value : DBNull.Value);
            command.AddParameter("@ToDate", toDate.HasValue ? toDate.Value : DBNull.Value);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                analytics.Summary = new AiUsageSummaryTotalsDto
                {
                    TotalEvents = reader.GetInt64Value("TotalEvents"),
                    TotalPromptTokens = reader.GetInt64Value("TotalPromptTokens"),
                    TotalCompletionTokens = reader.GetInt64Value("TotalCompletionTokens"),
                    TotalTokens = reader.GetInt64Value("TotalTokens"),
                    TotalCostUsd = reader.GetDecimalValue("TotalCostUsd"),
                    AvgDurationMs = reader.GetDoubleValue("AvgDurationMs")
                };
            }

            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    analytics.TimeSeries.Add(new AiUsageTimeSeriesPointDto
                    {
                        Date = reader.GetDateTimeValue("Date"),
                        RequestsCount = reader.GetInt64Value("RequestsCount"),
                        TotalTokens = reader.GetInt64Value("TotalTokens"),
                        CostUsd = reader.GetDecimalValue("CostUsd")
                    });
                }
            }

            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    analytics.ModelBreakdown.Add(new AiUsageModelBreakdownDto
                    {
                        Provider = reader.GetStringOrEmpty("Provider"),
                        Model = reader.GetStringOrEmpty("Model"),
                        RequestsCount = reader.GetInt64Value("RequestsCount"),
                        TotalTokens = reader.GetInt64Value("TotalTokens"),
                        CostUsd = reader.GetDecimalValue("CostUsd")
                    });
                }
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        return analytics;
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

    private static AiUsageLogReadDto MapUsageLogRead(DbDataReader reader)
    {
        return new AiUsageLogReadDto
        {
            Id = reader.GetInt64Value("Id"),
            AiApiKeyId = reader.GetInt32Value("AiApiKeyId"),
            KeyName = reader.GetStringOrEmpty("KeyName"),
            KeyPrefix = reader.GetStringOrEmpty("KeyPrefix"),
            UserId = reader.GetInt32Value("UserId"),
            Provider = reader.GetStringOrEmpty("Provider"),
            Model = reader.GetStringOrEmpty("Model"),
            Endpoint = reader.GetStringOrEmpty("Endpoint"),
            Mode = reader.GetNullableString("Mode"),
            PromptTokens = reader.GetInt64Value("PromptTokens"),
            CompletionTokens = reader.GetInt64Value("CompletionTokens"),
            TotalTokens = reader.GetInt64Value("TotalTokens"),
            ApproxCostUsd = reader.GetDecimalValue("ApproxCostUsd"),
            DurationMs = reader.GetNullableInt32("DurationMs"),
            StatusCode = reader.GetInt32Value("StatusCode"),
            CreatedAt = reader.GetDateTimeValue("CreatedAt")
        };
    }
}
