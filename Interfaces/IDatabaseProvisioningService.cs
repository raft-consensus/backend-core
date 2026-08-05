using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IDatabaseProvisioningService
{
    string Engine { get; }

    bool IsAvailable { get; }

    int MaxDatabasesPerUser { get; }

    Task<DatabaseProvisioningResultDto> ProvisionDatabaseAsync(int userId, CancellationToken cancellationToken = default);

    Task PauseAsync(int databaseInstanceId, CancellationToken cancellationToken = default);

    Task ResumeAsync(int databaseInstanceId, CancellationToken cancellationToken = default);

    Task DeleteAsync(int databaseInstanceId, CancellationToken cancellationToken = default);

    Task<long> GetUsedSpaceBytesAsync(string databaseName, CancellationToken cancellationToken = default);

    Task<int> GetActiveConnectionCountAsync(string databaseName, CancellationToken cancellationToken = default);
}
