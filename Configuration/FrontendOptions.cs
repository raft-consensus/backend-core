namespace raft_backend.Configuration;

public class FrontendOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string CallbackPath { get; set; } = "/auth/callback";

    public string Origin => BaseUrl.TrimEnd('/');
    public string CallbackUrl => $"{Origin}{CallbackPath}";
}
