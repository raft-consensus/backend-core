using raft_backend.DTOs;

namespace raft_backend.Interfaces;

public interface IUserDashboardService
{
    Task<IReadOnlyList<UserDashboardDto>> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default);
}
