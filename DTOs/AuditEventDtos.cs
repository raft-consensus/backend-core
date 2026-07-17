namespace raft_backend.DTOs;

public class AuditEventCreateDto
{
    public int? UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? AdditionalData { get; set; }
}

public class AuditEventUpdateDto
{
    public int? UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? AdditionalData { get; set; }
}

public class AuditEventReadDto
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? AdditionalData { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
