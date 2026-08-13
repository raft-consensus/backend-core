using System.Data.Common;
using raft_backend.Database;
using raft_backend.DTOs;

namespace raft_backend.Services;

public class PlatformMetricsService : IPlatformMetricsService
{
    private readonly ISqlStoredProcedureExecutor _executor;
    private readonly IApiAvailabilityTracker _availabilityTracker;

    public PlatformMetricsService(ISqlStoredProcedureExecutor executor, IApiAvailabilityTracker availabilityTracker)
    {
        _executor = executor;
        _availabilityTracker = availabilityTracker;
    }

    public Task<PlatformMetricsDto> GetPlatformMetricsAsync(CancellationToken cancellationToken = default)
    {
        return _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.PlatformMetrics_Get,
            null,
            Map,
            cancellationToken).ContinueWith(static task => task.Result ?? new PlatformMetricsDto(), cancellationToken);
    }

    private PlatformMetricsDto Map(DbDataReader reader)
    {
        return new PlatformMetricsDto
        {
            TotalUsers = reader.GetInt32Value("TotalUsers"),
            TotalDatabases = reader.GetInt32Value("TotalDatabases"),
            ActiveDatabases = reader.GetInt32Value("ActiveDatabases"),
            TotalSubdomains = reader.GetInt32Value("TotalSubdomains"),
            TotalAiRequests = reader.GetInt64Value("TotalAiRequests"),
            TotalN8nExecutions = reader.GetInt64Value("TotalN8nExecutions"),
            TotalSecureOperations = reader.GetInt32Value("TotalSecureOperations"),
            TotalLogins = reader.GetInt32Value("TotalLogins"),
            ActiveUsers = reader.GetInt32Value("ActiveUsers"),
            ServiceAvailability = _availabilityTracker.GetAvailabilityPercent()
        };
    }
}
