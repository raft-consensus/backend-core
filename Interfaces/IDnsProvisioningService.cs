using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IDnsProvisioningService
{
    bool IsAvailable { get; }

    int MaxRecordsPerUser { get; }

    Task<IReadOnlyList<DnsRecordReadDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DnsRecordReadDto>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken = default);

    Task<DnsRecordReadDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<DnsRecordReadDto?> GetByIdForUserAsync(int userId, int id, CancellationToken cancellationToken = default);

    Task<DnsRecordReadDto?> GetActiveByUserIdAndFqdnAsync(int userId, string fqdn, CancellationToken cancellationToken = default);

    Task<DnsProvisioningResultDto?> ProvisionAsync(int userId, DnsRecordCreateDto dto, CancellationToken cancellationToken = default);

    Task<DnsRecordReadDto?> UpdateAsync(int userId, int id, DnsRecordUpdateDto dto, CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(int userId, int id, CancellationToken cancellationToken = default);
}
