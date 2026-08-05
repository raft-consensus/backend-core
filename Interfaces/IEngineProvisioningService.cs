using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IEngineProvisioningService
{
    string EngineName { get; }

    Task<SqlServerProvisioningResultDto> ProvisionAsync(int userId, CancellationToken cancellationToken = default);
}
