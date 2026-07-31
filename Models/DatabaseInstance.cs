namespace raft_backend.Models;

public class DatabaseInstance
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string DatabaseUser { get; set; } = string.Empty;
    public string Engine { get; set; } = "SQL Server";
    public string Status { get; set; } = string.Empty; // "Active" | "Suspended" | "Deleted"
    public long UsedSpaceBytes { get; set; }
    public long MaxSpaceBytes { get; set; } // 20 MB according to deliverable 1
    public DateTime? LastActivity { get; set; }
    public DateTime Created_at { get; set; }
    public DateTime? Updated_at { get; set; }
    public DateTime? Deleted_at { get; set; }

    public User? User { get; set; }
    public AccessCredential? AccessCredential { get; set; }
}
