using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface ISqlServerProvisioningService
{
    Task<SqlServerProvisioningResultDto> ProvisionDatabaseAsync(int userId, CancellationToken cancellationToken = default);

    Task PauseAsync(int databaseInstanceId, CancellationToken cancellationToken = default);

    Task ResumeAsync(int databaseInstanceId, CancellationToken cancellationToken = default);

    Task DeleteAsync(int databaseInstanceId, CancellationToken cancellationToken = default);
}
