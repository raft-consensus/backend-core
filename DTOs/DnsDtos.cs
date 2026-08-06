namespace raft_backend.DTOs;

public class DnsRecordCreateDto
{
    public string Label { get; set; } = string.Empty;

    public string Subdomain
    {
        get => Label;
        set => Label = value;
    }

    public string? Content { get; set; }
    public string? Comment { get; set; }
    public int? RecordTtl { get; set; }
    public bool? Proxied { get; set; } = false;
}

public class DnsRecordUpdateDto
{
    public string? Label { get; set; }

    public string? Subdomain
    {
        get => Label;
        set => Label = value;
    }

    public string? Content { get; set; }
    public string? Comment { get; set; }
    public int? RecordTtl { get; set; }
    public bool? Proxied { get; set; }
}

public class DnsRecordReadDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Subdomain => Label;
    public string RecordName { get; set; } = string.Empty;
    public string Fqdn { get; set; } = string.Empty;
    public string RecordType { get; set; } = "A";
    public string Content { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public int RecordTtl { get; set; }
    public bool Proxied { get; set; }
    public string SslStatus => Proxied ? "Active (Cloudflare SSL Proxied)" : "DNS Only";
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
