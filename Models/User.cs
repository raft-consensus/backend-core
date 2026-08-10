namespace raft_backend.Models;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    
    public string? PasswordHash { get; set; }
    public string? TemporaryPasswordHash { get; set; }
    public DateTime? TemporaryPasswordExpires_at { get; set; }
    public string? AvatarUrl { get; set; }
    public string Provider { get; set; } = string.Empty; // "Google" | "GitHub"
    public string ProviderUserId { get; set; } = string.Empty; // unique identifier provided by Google/GitHub (sub / id)
    public string Role { get; set; } = "User"; // "User" | "Admin"
    public DateTime Created_at { get; set; }
    public DateTime? Updated_at { get; set; }
    public DateTime? Deleted_at { get; set; }
    public DateTime? LastLogin { get; set; }

    public ICollection<DatabaseInstance> DatabaseInstances { get; set; } = new List<DatabaseInstance>();
    public ICollection<AuditEvent> AuditEvents { get; set; } = new List<AuditEvent>();
}
