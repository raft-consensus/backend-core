namespace raft_backend.DTOs;

public class DnsRecordCreateDto
{
    public string Label { get; set; } = string.Empty;
    public string? Content { get; set; }
    public int? RecordTtl { get; set; }
    public bool? Proxied { get; set; }
}

public class DnsRecordReadDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string RecordName { get; set; } = string.Empty;
    public string Fqdn { get; set; } = string.Empty;
    public string RecordType { get; set; } = "A";
    public string Content { get; set; } = string.Empty;
    public int RecordTtl { get; set; }
    public bool Proxied { get; set; }
    public string? CloudflareZoneId { get; set; }
    public string? CloudflareRecordId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class DnsProvisioningResultDto
{
    public bool Created { get; set; }
    public DnsRecordReadDto Record { get; set; } = new();
}
