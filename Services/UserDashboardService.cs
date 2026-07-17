using System.Data.Common;
using raft_backend.Database;
using raft_backend.DTOs;

namespace raft_backend.Services;

public class UserDashboardService : IUserDashboardService
{
    private readonly ISqlStoredProcedureExecutor _executor;

    public UserDashboardService(ISqlStoredProcedureExecutor executor)
    {
        _executor = executor;
    }

    public async Task<IReadOnlyList<UserDashboardDto>> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        var items = await _executor.QueryAsync(
            StoredProcedureNames.UserDashboard_GetByUserId,
            command => command.AddParameter("@UserId", userId),
            Map,
            cancellationToken);

        return items;
    }

    private static UserDashboardDto Map(DbDataReader reader)
    {
        return new UserDashboardDto
        {
            DatabaseInstanceId = reader.GetInt32Value("DatabaseInstanceId"),
            Host = reader.GetStringOrEmpty("Host"),
            Port = reader.GetInt32Value("Port"),
            DatabaseName = reader.GetStringOrEmpty("DatabaseName"),
            DatabaseUser = reader.GetStringOrEmpty("DatabaseUser"),
            Engine = reader.GetStringOrEmpty("Engine"),
            Status = reader.GetStringOrEmpty("Status"),
            UsedSpaceBytes = reader.GetInt64Value("UsedSpaceBytes"),
            MaxSpaceBytes = reader.GetInt64Value("MaxSpaceBytes"),
            LastActivity = reader.GetNullableDateTime("LastActivity"),
            CreatedAt = reader.GetDateTimeValue("CreatedAt")
        };
    }
}
