using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IAccessCredentialService
{
    Task<IReadOnlyList<AccessCredentialReadDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AccessCredentialReadDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<AccessCredentialReadDto?> GetByDatabaseInstanceIdAsync(int databaseInstanceId, CancellationToken cancellationToken = default);
    Task<AccessCredentialReadDto?> CreateAsync(AccessCredentialCreateDto dto, CancellationToken cancellationToken = default);
    Task<AccessCredentialReadDto?> UpdateAsync(int id, AccessCredentialUpdateDto dto, CancellationToken cancellationToken = default);
    Task<bool> SoftDeleteAsync(int id, CancellationToken cancellationToken = default);
}
