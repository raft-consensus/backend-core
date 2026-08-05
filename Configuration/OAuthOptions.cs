using System.ComponentModel.DataAnnotations;

namespace raft_backend.Configuration;

public class OAuthOptions
{
    [Required]
    public string GoogleClientId { get; set; } = string.Empty;

    [Required]
    public string GoogleClientSecret { get; set; } = string.Empty;

    [Required]
    public string GitHubClientId { get; set; } = string.Empty;

    [Required]
    public string GitHubClientSecret { get; set; } = string.Empty;
}
