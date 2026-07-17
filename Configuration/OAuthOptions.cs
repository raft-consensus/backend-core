namespace raft_backend.Configuration;

public class OAuthOptions
{
    public string GoogleClientId { get; set; } = string.Empty;
    public string GoogleClientSecret { get; set; } = string.Empty;
    public string GitHubClientId { get; set; } = string.Empty;
    public string GitHubClientSecret { get; set; } = string.Empty;
}
