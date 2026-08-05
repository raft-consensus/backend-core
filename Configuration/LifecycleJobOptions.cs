using System.ComponentModel.DataAnnotations;

namespace raft_backend.Configuration;

public class LifecycleJobOptions
{
    [Range(1, 24 * 60)]
    public int IntervalMinutes { get; set; } = 15;

    [Range(1, 3650)]
    public int InactivityPauseDays { get; set; } = 7;

    [Range(1, 3650)]
    public int InactivityDeleteDays { get; set; } = 30;

    [Range(1, 1000)]
    public int MaxConcurrentConnectionsPerDatabase { get; set; } = 5;
}
