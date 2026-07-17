namespace raft_backend.DTOs;

public class UserDashboardDto
{
    public int DatabaseInstanceId { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string DatabaseUser { get; set; } = string.Empty;
    public string Engine { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long UsedSpaceBytes { get; set; }
    public long MaxSpaceBytes { get; set; }
    public DateTime? LastActivity { get; set; }
    public DateTime CreatedAt { get; set; }
}
