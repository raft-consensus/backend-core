namespace raft_backend.Configuration;

public class LifecycleJobOptions
{
    public int IntervalMinutes { get; set; } = 15;
    public int InactivityPauseDays { get; set; } = 7;
    public int InactivityDeleteDays { get; set; } = 30;
}
