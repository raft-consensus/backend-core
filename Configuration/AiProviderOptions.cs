using System.ComponentModel.DataAnnotations;

namespace raft_backend.Configuration;

public class AiProviderOptions
{
    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Endpoint { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    [Required, MaxLength(80)]
    public string Model { get; set; } = "gpt-4o-mini";

    [Range(0, 1000)]
    public int Priority { get; set; } = 100;

    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    [Range(1, 1000)]
    public int MaxOutputTokens { get; set; } = 512;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);
}
