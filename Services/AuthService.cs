using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using raft_backend.Configuration;
using raft_backend.Database;
using raft_backend.DTOs;
using raft_backend.Models;

namespace raft_backend.Services;

public class AuthService : IAuthService
{
    private readonly RaftDbContext _context;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        RaftDbContext context,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthService> logger)
    {
        _context = context;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<AuthResponseDto?> CompleteExternalLoginAsync(string provider, ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var normalizedProvider = NormalizeProvider(provider);
        if (normalizedProvider is null)
        {
            _logger.LogWarning("Unsupported OAuth provider: {Provider}", provider);
            return null;
        }

        var providerUserId = GetProviderUserId(principal);
        var email = GetEmail(principal, normalizedProvider, providerUserId);
        var name = GetName(principal, email, providerUserId);
        var avatarUrl = GetAvatarUrl(principal);

        var user = await _context.Users
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Provider == normalizedProvider && x.ProviderUserId == providerUserId, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Name = name,
                Email = email,
                AvatarUrl = avatarUrl,
                Provider = normalizedProvider,
                ProviderUserId = providerUserId,
                Created_at = DateTime.UtcNow,
                LastLogin = DateTime.UtcNow
            };

            _context.Users.Add(user);
        }
        else
        {
            user.Name = name;
            user.Email = email;
            user.AvatarUrl = avatarUrl;
            user.Provider = normalizedProvider;
            user.ProviderUserId = providerUserId;
            user.LastLogin = DateTime.UtcNow;
            user.Updated_at = DateTime.UtcNow;
            user.Deleted_at = null;
        }

        _context.AuditEvents.Add(new AuditEvent
        {
            User = user,
            EventType = "Login",
            Description = $"OAuth login completed with {normalizedProvider}.",
            AdditionalData = $$"""{"provider":"{{normalizedProvider}}","providerUserId":"{{providerUserId}}"}""",
            Created_at = DateTime.UtcNow
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist OAuth login for provider {Provider} and subject {ProviderUserId}", normalizedProvider, providerUserId);
            throw;
        }

        var token = CreateJwt(user);

        return new AuthResponseDto
        {
            AccessToken = token.Token,
            ExpiresAt = token.ExpiresAt,
            Provider = normalizedProvider,
            User = new UserReadDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                Provider = user.Provider,
                ProviderUserId = user.ProviderUserId,
                CreatedAt = user.Created_at,
                UpdatedAt = user.Updated_at,
                DeletedAt = user.Deleted_at,
                LastLogin = user.LastLogin
            }
        };
    }

    private JwtTokenResult CreateJwt(User user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpirationMinutes);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new("provider", user.Provider),
            new("provider_user_id", user.ProviderUserId)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private static string NormalizeProvider(string provider)
    {
        return provider.Trim().ToLowerInvariant() switch
        {
            "google" => "Google",
            "github" => "GitHub",
            _ => string.Empty
        };
    }

    private static string GetProviderUserId(ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub")
            ?? principal.FindFirstValue("id")
            ?? throw new InvalidOperationException("OAuth principal is missing a provider user id.");
    }

    private static string GetEmail(ClaimsPrincipal principal, string provider, string providerUserId)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email");

        if (!string.IsNullOrWhiteSpace(email))
        {
            return email;
        }

        if (provider == "GitHub")
        {
            return $"{providerUserId}@users.noreply.github.local";
        }

        throw new InvalidOperationException("OAuth principal is missing an email address.");
    }

    private static string GetName(ClaimsPrincipal principal, string email, string providerUserId)
    {
        return principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? principal.FindFirstValue("login")
            ?? email.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            ?? providerUserId;
    }

    private static string? GetAvatarUrl(ClaimsPrincipal principal)
    {
        return principal.FindFirstValue("picture")
            ?? principal.FindFirstValue("urn:github:avatar")
            ?? principal.FindFirstValue("avatar_url");
    }

    private sealed record JwtTokenResult(string Token, DateTime ExpiresAt);
}
