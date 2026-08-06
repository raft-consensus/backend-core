using System.ComponentModel.DataAnnotations;

namespace raft_backend.Configuration;

public class N8nProvisioningOptions
{
    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; set; } = 30;
}
