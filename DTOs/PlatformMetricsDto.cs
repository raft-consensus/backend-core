namespace raft_backend.DTOs;

public class PlatformMetricsDto
{
    public int TotalUsers { get; set; }
    public int TotalDatabases { get; set; }
    public int ActiveDatabases { get; set; }
    public int TotalLogins { get; set; }
    public int ActiveUsers { get; set; }
    public decimal ServiceAvailability { get; set; }
}
