using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IAiApiKeyService
{
    Task<IReadOnlyList<AiApiKeyReadDto>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken = default);
    Task<AiApiKeyReadDto?> GetByIdAsync(int userId, int id, CancellationToken cancellationToken = default);
    Task<AiApiKeySecretDto?> CreateAsync(int userId, AiApiKeyCreateDto dto, CancellationToken cancellationToken = default);
    Task<AiApiKeySecretDto?> RotateAsync(int userId, int id, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(int userId, int id, CancellationToken cancellationToken = default);
    Task<AiApiKeyReadDto?> ResolveBySecretAsync(string apiKeySecret, CancellationToken cancellationToken = default);
    Task<bool> RecordUsageAsync(
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
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiUsageLogReadDto>> GetUsageHistoryAsync(
        int userId,
        AiUsageHistoryFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<AiUsageAnalyticsDto> GetUsageAnalyticsAsync(
        int userId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);
}
