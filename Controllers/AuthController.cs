using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.DTOs;
using raft_backend.Response;
using raft_backend.Services;

namespace raft_backend.Controllers;

[ApiController]
[EnableRateLimiting("auth")]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly FrontendOptions _frontendOptions;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, IOptions<FrontendOptions> frontendOptions, ILogger<AuthController> logger)
    {
        _authService = authService;
        _frontendOptions = frontendOptions.Value;
        _logger = logger;
    }

    [HttpGet("login/{provider}")]
    [AllowAnonymous]
    public IActionResult Login(string provider)
    {
        var scheme = GetScheme(provider);
        if (scheme is null)
        {
            return BadRequest(new ServiceResponse<object>
            {
                Success = false,
                Message = "Only Google and GitHub are supported."
            });
        }

        var redirectUri = Url.Action(nameof(Callback), "Auth", new { provider }, Request.Scheme);
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = redirectUri
        }, scheme);
    }
    
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterDto dto, CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterWithPasswordAsync(dto, cancellationToken);
        if (result is null)
        {
            return Conflict(new ServiceResponse<object>
            {
                Success = false,
                Message = "That email is already registered."
            });
        }

        return Ok(new ServiceResponse<AuthResponseDto>
        {
            Success = true,
            Message = "User registered successfully.",
            Data = result
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginWithPassword(LoginDto dto, CancellationToken cancellationToken)
    {
        var result = await _authService.LoginWithPasswordAsync(dto, cancellationToken);
        if (result is null)
        {
            return Unauthorized(new ServiceResponse<object>
            {
                Success = false,
                Message = "Invalid email or password."
            });
        }

        return Ok(new ServiceResponse<AuthResponseDto>
        {
            Success = true,
            Message = "Login successful.",
            Data = result
        });
    }

    // This endpoint is reached via a full browser navigation (OAuth redirect chain), never
    // via fetch/XHR from the SPA — so it must hand off with an HTTP redirect back to the
    // frontend, not a JSON body the frontend could never intercept.
    [HttpGet("callback/{provider}")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(string provider, CancellationToken cancellationToken)
    {
        var authResult = await HttpContext.AuthenticateAsync(AuthSchemes.External);
        if (!authResult.Succeeded || authResult.Principal is null)
        {
            return RedirectToFrontendWithError("oauth_failed");
        }

        try
        {
            var result = await _authService.CompleteExternalLoginAsync(provider, authResult.Principal, cancellationToken);
            await HttpContext.SignOutAsync(AuthSchemes.External);

            if (result is null)
            {
                return RedirectToFrontendWithError("unsupported_provider");
            }

            return RedirectToFrontendWithToken(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth callback failed for provider {Provider}", provider);
            return RedirectToFrontendWithError("login_failed");
        }
    }

    private IActionResult RedirectToFrontendWithToken(AuthResponseDto result)
    {
        var fragment = $"access_token={Uri.EscapeDataString(result.AccessToken)}" +
            $"&expires_at={Uri.EscapeDataString(result.ExpiresAt.ToString("o"))}" +
            $"&provider={Uri.EscapeDataString(result.Provider)}";

        return Redirect($"{_frontendOptions.CallbackUrl}#{fragment}");
    }

    private IActionResult RedirectToFrontendWithError(string errorCode)
    {
        return Redirect($"{_frontendOptions.CallbackUrl}#error={Uri.EscapeDataString(errorCode)}");
    }

    private static string? GetScheme(string provider)
    {
        return provider.Trim().ToLowerInvariant() switch
        {
            "google" => AuthSchemes.Google,
            "github" => AuthSchemes.GitHub,
            _ => null
        };
    }
}
