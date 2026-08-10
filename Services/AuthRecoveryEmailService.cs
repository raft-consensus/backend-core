using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public class AuthRecoveryEmailService : IAuthRecoveryEmailService
{
    private readonly N8nProvisioningOptions _options;
    private readonly FrontendOptions _frontendOptions;
    private readonly ILogger<AuthRecoveryEmailService> _logger;

    public AuthRecoveryEmailService(
        IOptions<N8nProvisioningOptions> options,
        IOptions<FrontendOptions> frontendOptions,
        ILogger<AuthRecoveryEmailService> logger)
    {
        _options = options.Value;
        _frontendOptions = frontendOptions.Value;
        _logger = logger;
    }

    public async Task SendTemporaryPasswordAsync(
        string email,
        string name,
        string temporaryPassword,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds)
        };

        client.DefaultRequestHeaders.Add("X-Api-Key", _options.ApiKey);

        var payload = new PasswordRecoveryRequestDto
        {
            Email = email,
            Name = name,
            TemporaryPassword = temporaryPassword,
            ExpiresAt = expiresAt.ToUniversalTime().ToString("o"),
            FrontendUrl = _frontendOptions.Origin
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildRecoveryUrl())
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = string.IsNullOrWhiteSpace(responseBody)
                ? $"N8N password recovery returned HTTP {(int)response.StatusCode}."
                : $"N8N password recovery returned HTTP {(int)response.StatusCode}: {responseBody}";

            _logger.LogWarning("Password recovery email delivery failed for {Email}: {Message}", email, message);
            throw new InvalidOperationException(Sanitize(message));
        }
    }

    private string BuildRecoveryUrl()
    {
        var baseUrl = _options.BaseUrl.Trim();
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        return new Uri(new Uri(baseUrl), "n8n/external/password-recovery").ToString();
    }

    private static string Sanitize(string value)
    {
        return value.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private sealed class PasswordRecoveryRequestDto
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("temporary_password")]
        public string TemporaryPassword { get; set; } = string.Empty;

        [JsonPropertyName("expires_at")]
        public string ExpiresAt { get; set; } = string.Empty;

        [JsonPropertyName("frontend_url")]
        public string FrontendUrl { get; set; } = string.Empty;
    }
}
