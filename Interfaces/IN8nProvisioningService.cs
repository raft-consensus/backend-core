using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IN8nProvisioningService
{
    Task<IReadOnlyList<N8nAccountReadDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<N8nAccountReadDto>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken = default);

    Task<N8nAccountReadDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<N8nAccountReadDto?> GetActiveByUserIdAsync(int userId, CancellationToken cancellationToken = default);

    Task<N8nProvisioningResultDto?> ProvisionAsync(int userId, CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(int id, CancellationToken cancellationToken = default);
}
