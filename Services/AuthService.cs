using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using raft_backend.Configuration;
using raft_backend.Database;
using raft_backend.DTOs;
using raft_backend.Interfaces;

namespace raft_backend.Services;

public class AuthService : IAuthService
{
    private readonly ISqlStoredProcedureExecutor _executor;
    private readonly JwtOptions _jwtOptions;
    private readonly IAuthRecoveryEmailService _recoveryEmailService;
    private readonly IAuditEventService _auditEventService;
    private readonly ISecurePasswordGenerator _passwordGenerator;
    private readonly ILogger<AuthService> _logger;
    private const int TemporaryPasswordLength = 24;
    private const int TemporaryPasswordLifetimeMinutes = 30;

    public AuthService(
        ISqlStoredProcedureExecutor executor,
        IOptions<JwtOptions> jwtOptions,
        IAuthRecoveryEmailService recoveryEmailService,
        IAuditEventService auditEventService,
        ISecurePasswordGenerator passwordGenerator,
        ILogger<AuthService> logger)
    {
        _executor = executor;
        _jwtOptions = jwtOptions.Value;
        _recoveryEmailService = recoveryEmailService;
        _auditEventService = auditEventService;
        _passwordGenerator = passwordGenerator;
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
            MapPasswordLookupResult,
            cancellationToken);

        if (result is null)
        {
            return null; // no existe ese email
        }

        if (!result.HasLocalPassword || result.PasswordHash is null)
        {
            return null; // se registró por OAuth y nunca tuvo password local
        }

        var passwordMatchesPermanent = BCrypt.Net.BCrypt.Verify(dto.Password, result.PasswordHash);
        var passwordMatchesTemporary = IsTemporaryPasswordValid(result) &&
            result.TemporaryPasswordHash is not null &&
            BCrypt.Net.BCrypt.Verify(dto.Password, result.TemporaryPasswordHash);

        if (!passwordMatchesPermanent && !passwordMatchesTemporary)
        {
            return null; // password incorrecto
        }

        if (passwordMatchesTemporary)
        {
            result.User.PasswordChangeRequired = true;
        }
        else
        {
            result.User.PasswordChangeRequired = result.PasswordChangeRequired;
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

    public async Task<bool> RequestTemporaryPasswordAsync(RequestTemporaryPasswordDto dto, CancellationToken cancellationToken = default)
    {
        var lookup = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_GetByEmailForLogin,
            command => command.AddParameter("@Email", dto.Email),
            MapPasswordLookupResult,
            cancellationToken);

        if (lookup is null || !lookup.HasLocalPassword || lookup.PasswordHash is null)
        {
            return false;
        }

        var temporaryPassword = _passwordGenerator.Generate(TemporaryPasswordLength);
        var temporaryPasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);
        var expiresAt = DateTime.UtcNow.AddMinutes(TemporaryPasswordLifetimeMinutes);

        var updated = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_SetTemporaryPassword,
            command =>
            {
                command.AddParameter("@UserId", lookup.User.Id);
                command.AddParameter("@TemporaryPasswordHash", temporaryPasswordHash);
                command.AddParameter("@TemporaryPasswordExpiresAt", expiresAt);
            },
            MapUserReadDto,
            cancellationToken);

        if (updated is null)
        {
            return false;
        }

        await _recoveryEmailService.SendTemporaryPasswordAsync(
            updated.Email,
            updated.Name,
            temporaryPassword,
            expiresAt,
            cancellationToken);

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = updated.Id,
            EventType = "TemporaryPasswordRequested",
            Description = "User requested a temporary password.",
            AdditionalData = $"{{\"expiresAt\":\"{expiresAt:o}\"}}"
        }, cancellationToken);

        return true;
    }

    public async Task<UserReadDto?> ChangePasswordAsync(int userId, ChangePasswordDto dto, CancellationToken cancellationToken = default)
    {
        var current = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_GetPasswordStateById,
            command => command.AddParameter("@UserId", userId),
            MapPasswordLookupResult,
            cancellationToken);

        if (current is null || !current.HasLocalPassword || current.PasswordHash is null)
        {
            return null;
        }

        var currentMatchesPermanent = BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, current.PasswordHash);
        var currentMatchesTemporary = IsTemporaryPasswordValid(current) &&
            current.TemporaryPasswordHash is not null &&
            BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, current.TemporaryPasswordHash);

        if (!currentMatchesPermanent && !currentMatchesTemporary)
        {
            return null;
        }

        var newPasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);

        var updated = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_ChangeLocalPassword,
            command =>
            {
                command.AddParameter("@UserId", userId);
                command.AddParameter("@PasswordHash", newPasswordHash);
            },
            MapUserReadDto,
            cancellationToken);

        if (updated is null)
        {
            return null;
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = updated.Id,
            EventType = "PasswordChanged",
            Description = "User changed their local password.",
            AdditionalData = $"{{\"changedAt\":\"{DateTime.UtcNow:o}\"}}"
        }, cancellationToken);

        return updated;
    }

    public async Task<UserReadDto?> SetLocalPasswordAsync(int userId, SetLocalPasswordDto dto, CancellationToken cancellationToken = default)
    {
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        var updated = await _executor.QuerySingleOrDefaultAsync(
            StoredProcedureNames.Users_SetLocalPassword,
            command =>
            {
                command.AddParameter("@UserId", userId);
                command.AddParameter("@PasswordHash", passwordHash);
            },
            MapUserReadDto,
            cancellationToken);

        if (updated is not null)
        {
            await _auditEventService.CreateAsync(new AuditEventCreateDto
            {
                UserId = updated.Id,
                EventType = "LocalPasswordLinked",
                Description = "User enabled a local password for this account."
            }, cancellationToken);
        }

        return updated;
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
            HasLocalPassword = reader.GetBooleanValue("HasLocalPassword"),
            PasswordChangeRequired = reader.GetBooleanValue("PasswordChangeRequired"),
            CreatedAt = reader.GetDateTimeValue("CreatedAt"),
            UpdatedAt = reader.GetNullableDateTime("UpdatedAt"),
            DeletedAt = reader.GetNullableDateTime("DeletedAt"),
            LastLogin = reader.GetNullableDateTime("LastLogin")
        };
    }

    private static PasswordLookupResult MapPasswordLookupResult(DbDataReader reader)
    {
        var user = MapUserReadDto(reader);
        var passwordHash = reader.GetNullableString("PasswordHash");
        var temporaryPasswordHash = reader.GetNullableString("TemporaryPasswordHash");
        var temporaryPasswordExpiresAt = reader.GetNullableDateTime("TemporaryPasswordExpiresAt");
        var hasLocalPassword = reader.GetBooleanValue("HasLocalPassword");
        var passwordChangeRequired = reader.GetBooleanValue("PasswordChangeRequired");
        return new PasswordLookupResult(
            user,
            passwordHash,
            temporaryPasswordHash,
            temporaryPasswordExpiresAt,
            hasLocalPassword,
            passwordChangeRequired);
    }

    private sealed record OAuthUpsertResult(UserReadDto User, bool IsNewUser);

    private sealed record PasswordLookupResult(
        UserReadDto User,
        string? PasswordHash,
        string? TemporaryPasswordHash,
        DateTime? TemporaryPasswordExpiresAt,
        bool HasLocalPassword,
        bool PasswordChangeRequired);

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

    private static bool IsTemporaryPasswordValid(PasswordLookupResult result)
    {
        return result.TemporaryPasswordExpiresAt.HasValue &&
            result.TemporaryPasswordExpiresAt.Value > DateTime.UtcNow &&
            !string.IsNullOrWhiteSpace(result.TemporaryPasswordHash);
    }

    private sealed record JwtTokenResult(string Token, DateTime ExpiresAt);
}
