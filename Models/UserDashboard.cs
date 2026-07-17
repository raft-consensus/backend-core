namespace raft_backend.Models;

public class UserDashboard
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long UsedSpaceBytes { get; set; }
    public long MaxSpaceBytes { get; set; }
    public DateTime? LastActivity { get; set; }
    public DateTime Created_at { get; set; }
}