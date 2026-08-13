namespace raft_backend.Models;

public class PlatformMetrics
{
    public int TotalUsers { get; set; }
    public int TotalDatabases { get; set; }
    public int ActiveDatabases { get; set; }
    public int TotalSubdomains { get; set; }
    public long TotalAiRequests { get; set; }
    public long TotalN8nExecutions { get; set; }
    public int TotalSecureOperations { get; set; }
    public int TotalLogins { get; set; }
    public int ActiveUsers { get; set; }
    public decimal ServiceAvailability { get; set; } // porcentaje uptime
}