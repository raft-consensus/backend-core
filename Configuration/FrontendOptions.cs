using System.ComponentModel.DataAnnotations;

namespace raft_backend.Configuration;

public class FrontendOptions
{
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    public string CallbackPath { get; set; } = "/auth/callback";

    public string[] Origins { get; set; } = [];

    public string Origin => NormalizeOrigin(BaseUrl);

    public string CallbackUrl => $"{Origin}{CallbackPath}";

    public IReadOnlyList<string> GetAllowedOrigins()
    {
        var normalizedOrigins = Origins
            .Select(NormalizeOrigin)
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedOrigins.Length > 0)
        {
            return normalizedOrigins;
        }

        var fallbackOrigin = NormalizeOrigin(BaseUrl);
        return string.IsNullOrWhiteSpace(fallbackOrigin)
            ? Array.Empty<string>()
            : [fallbackOrigin];
    }

    private static string NormalizeOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : trimmed.TrimEnd('/');
    }
}
