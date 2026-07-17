using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IAuditEventService
{
    Task<IReadOnlyList<AuditEventReadDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AuditEventReadDto?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<AuditEventReadDto?> CreateAsync(AuditEventCreateDto dto, CancellationToken cancellationToken = default);
    Task<AuditEventReadDto?> UpdateAsync(long id, AuditEventUpdateDto dto, CancellationToken cancellationToken = default);
    Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken = default);
}
