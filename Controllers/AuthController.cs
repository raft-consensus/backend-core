using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
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

    [HttpGet("callback/{provider}")]
    [AllowAnonymous]
    public async Task<ActionResult<ServiceResponse<AuthResponseDto>>> Callback(string provider, CancellationToken cancellationToken)
    {
        var authResult = await HttpContext.AuthenticateAsync(AuthSchemes.External);
        if (!authResult.Succeeded || authResult.Principal is null)
        {
            return Unauthorized(new ServiceResponse<AuthResponseDto>
            {
                Success = false,
                Message = "External authentication could not be completed."
            });
        }

        try
        {
            var result = await _authService.CompleteExternalLoginAsync(provider, authResult.Principal, cancellationToken);
            await HttpContext.SignOutAsync(AuthSchemes.External);

            if (result is null)
            {
                return BadRequest(new ServiceResponse<AuthResponseDto>
                {
                    Success = false,
                    Message = "Unsupported OAuth provider or invalid external identity."
                });
            }

            return Ok(new ServiceResponse<AuthResponseDto>
            {
                Success = true,
                Message = "OAuth authentication completed successfully.",
                Data = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth callback failed for provider {Provider}", provider);
            return StatusCode(StatusCodes.Status500InternalServerError, new ServiceResponse<AuthResponseDto>
            {
                Success = false,
                Message = "OAuth login failed."
            });
        }
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
