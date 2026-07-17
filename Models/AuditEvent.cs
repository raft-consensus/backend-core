namespace raft_backend.Models;

public class AuditEvent
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string EventType { get; set; } = string.Empty; // "Login", "Provisioning", "Error", etc.
    public string Description { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? AdditionalData { get; set; } // JSON crudo si necesitas contexto extra
    public DateTime Created_at { get; set; }
    public DateTime? Deleted_at { get; set; }

    public User? User { get; set; }
}
