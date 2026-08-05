using System.ComponentModel.DataAnnotations;

namespace raft_backend.Configuration;

public class AiServiceOptions
{
    public List<AiProviderOptions> Providers { get; set; } = [];

    public string? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    [Range(1, 1000)]
    public int MaxOutputTokens { get; set; } = 512;

    [Range(1, 120)]
    public int ApiKeyRateLimitPerMinute { get; set; } = 20;

    [Range(1, 120)]
    public int ManagementRateLimitPerMinute { get; set; } = 20;
}
