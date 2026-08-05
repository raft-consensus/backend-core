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
    Task<bool> RecordUsageAsync(int keyId, long promptTokens, long completionTokens, decimal approxCostUsd, CancellationToken cancellationToken = default);
}
