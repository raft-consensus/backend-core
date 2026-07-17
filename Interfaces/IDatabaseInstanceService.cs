using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IDatabaseInstanceService
{
    Task<IReadOnlyList<DatabaseInstanceReadDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<DatabaseInstanceReadDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<DatabaseInstanceReadDto?> CreateAsync(DatabaseInstanceCreateDto dto, CancellationToken cancellationToken = default);
    Task<DatabaseInstanceReadDto?> UpdateAsync(int id, DatabaseInstanceUpdateDto dto, CancellationToken cancellationToken = default);
    Task<bool> SoftDeleteAsync(int id, CancellationToken cancellationToken = default);
}
