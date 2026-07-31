using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using raft_backend.Configuration;
using raft_backend.Database;
using raft_backend.DTOs;

namespace raft_backend.Services;

public class AuthService : IAuthService
{
    private readonly ISqlStoredProcedureExecutor _executor;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        ISqlStoredProcedureExecutor executor,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthService> logger)
    {
        _executor = executor;
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

        // The lookup/insert/update decision, and the login audit entry, all happen inside
        // this single SP (usp_Users_UpsertFromOAuth) — this is the one real business flow
        // in the app, so it must be database-centric like everything else.
        var result = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_UpsertFromOAuth,
            command =>
            {
                command.AddParameter("@Provider", normalizedProvider);
                command.AddParameter("@ProviderUserId", providerUserId);
                command.AddParameter("@Name", name);
                command.AddParameter("@Email", email);
                command.AddParameter("@AvatarUrl", avatarUrl);
            },
            MapUpsertResult,
            cancellationToken);

        if (result is null)
        {
            throw new InvalidOperationException($"{StoredProcedureNames.Users_UpsertFromOAuth} did not return a user row.");
        }

        var user = result.User;

        var token = CreateJwt(user);

        return new AuthResponseDto
        {
            AccessToken = token.Token,
            ExpiresAt = token.ExpiresAt,
            Provider = normalizedProvider,
            User = user
        };
    }

    public async Task<AuthResponseDto?> RegisterWithPasswordAsync(RegisterDto dto, CancellationToken cancellationToken = default)
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        var user = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_RegisterWithPassword,
            command =>
            {
                command.AddParameter("@Name", dto.Name);
                command.AddParameter("@Email", dto.Email);
                command.AddParameter("@PasswordHash", passwordHash);
            },
            MapUserReadDto,
            cancellationToken);

        if (user is null)
        {
            // Email ya registrado: el SP no devolvió fila.
            return null;
        }

        var token = CreateJwt(user);

        return new AuthResponseDto
        {
            AccessToken = token.Token,
            ExpiresAt = token.ExpiresAt,
            Provider = "Password",
            User = user
        };
    }

    public async Task<AuthResponseDto?> LoginWithPasswordAsync(LoginDto dto, CancellationToken cancellationToken = default)
    {
        var result = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_GetByEmailForLogin,
            command => command.AddParameter("@Email", dto.Email),
            MapLoginLookupResult,
            cancellationToken);

        if (result is null)
        {
            return null; // no existe ese email
        }

        if (result.PasswordHash is null)
        {
            return null; // se registró por OAuth y nunca tuvo password
        }

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, result.PasswordHash))
        {
            return null; // password incorrecto
        }

        var token = CreateJwt(result.User);

        return new AuthResponseDto
        {
            AccessToken = token.Token,
            ExpiresAt = token.ExpiresAt,
            Provider = "Password",
            User = result.User
        };
    }

    private static OAuthUpsertResult MapUpsertResult(DbDataReader reader)
    {
        var user = MapUserReadDto(reader);
        return new OAuthUpsertResult(user, reader.GetBooleanValue("IsNewUser"));
    }

    private static UserReadDto MapUserReadDto(DbDataReader reader)
    {
        return new UserReadDto
        {
            Id = reader.GetInt32Value("Id"),
            Name = reader.GetStringOrEmpty("Name"),
            Email = reader.GetStringOrEmpty("Email"),
            AvatarUrl = reader.GetNullableString("AvatarUrl"),
            Provider = reader.GetNullableString("Provider") ?? string.Empty,
            ProviderUserId = reader.GetNullableString("ProviderUserId") ?? string.Empty,
            Role = reader.GetStringOrEmpty("Role"),
            CreatedAt = reader.GetDateTimeValue("CreatedAt"),
            UpdatedAt = reader.GetNullableDateTime("UpdatedAt"),
            DeletedAt = reader.GetNullableDateTime("DeletedAt"),
            LastLogin = reader.GetNullableDateTime("LastLogin")
        };
    }

    private static LoginLookupResult MapLoginLookupResult(DbDataReader reader)
    {
        var user = MapUserReadDto(reader);
        var passwordHash = reader.GetNullableString("PasswordHash");
        return new LoginLookupResult(user, passwordHash);
    }

    private sealed record OAuthUpsertResult(UserReadDto User, bool IsNewUser);

    private sealed record LoginLookupResult(UserReadDto User, string? PasswordHash);

    private JwtTokenResult CreateJwt(UserReadDto user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpirationMinutes);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
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
