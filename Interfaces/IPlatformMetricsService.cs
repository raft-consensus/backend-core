using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IPlatformMetricsService
{
    Task<PlatformMetricsDto> GetPlatformMetricsAsync(CancellationToken cancellationToken = default);
}
