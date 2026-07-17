using System.Data.Common;
using raft_backend.Database;
using raft_backend.DTOs;

namespace raft_backend.Services;

public class PlatformMetricsService : IPlatformMetricsService
{
    private readonly ISqlStoredProcedureExecutor _executor;

    public PlatformMetricsService(ISqlStoredProcedureExecutor executor)
    {
        _executor = executor;
    }

    public Task<PlatformMetricsDto> GetPlatformMetricsAsync(CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.PlatformMetrics_Get,
            null,
            Map,
            cancellationToken).ContinueWith(static task => task.Result ?? new PlatformMetricsDto(), cancellationToken);
    }

    private static PlatformMetricsDto Map(DbDataReader reader)
    {
        return new PlatformMetricsDto
        {
            TotalUsers = reader.GetInt32Value("TotalUsers"),
            TotalDatabases = reader.GetInt32Value("TotalDatabases"),
            ActiveDatabases = reader.GetInt32Value("ActiveDatabases"),
            TotalLogins = reader.GetInt32Value("TotalLogins"),
            ActiveUsers = reader.GetInt32Value("ActiveUsers"),
            ServiceAvailability = reader.GetDecimalValue("ServiceAvailability")
        };
    }
}
