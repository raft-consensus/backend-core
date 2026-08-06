using System.ComponentModel.DataAnnotations;

namespace raft_backend.Configuration;

public class DnsProvisioningOptions
{
    [Required]
    public string ZoneId { get; set; } = string.Empty;

    [Required]
    public string ZoneName { get; set; } = string.Empty;

    public string CellSubdomain { get; set; } = string.Empty;

    [Required]
    public string ApiToken { get; set; } = string.Empty;

    [Required]
    public string DefaultContent { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int RecordTtl { get; set; } = 1;

    public bool Proxied { get; set; } = false;

    [Range(1, 100)]
    public int MaxRecordsPerUser { get; set; } = 10;

    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; set; } = 30;
}
