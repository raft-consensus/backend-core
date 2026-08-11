using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using raft_backend.DTOs;
using raft_backend.Response;

namespace raft_backend.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("ai-api")]
[Route("api/ai")]
public class AiController : ControllerBase
{
    private readonly IAiService _service;

    public AiController(IAiService service)
    {
        _service = service;
    }

    [HttpPost("generate")]
    public async Task<ActionResult<ServiceResponse<AiGenerateResponseDto>>> Generate(
        AiGenerateRequestDto dto,
        CancellationToken cancellationToken)
    {
        var apiKey = Request.Headers["X-API-Key"].ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var authHeader = Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                apiKey = authHeader["Bearer ".Length..].Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unauthorized(new ServiceResponse<AiGenerateResponseDto>
            {
                Success = false,
                Message = "Missing Authorization (Bearer) or X-API-Key header."
            });
        }

        var result = await _service.GenerateAsync(apiKey, dto, cancellationToken);
        if (result is null)
        {
            return Unauthorized(new ServiceResponse<AiGenerateResponseDto>
            {
                Success = false,
                Message = "Invalid or inactive API key."
            });
        }

        return Ok(new ServiceResponse<AiGenerateResponseDto>
        {
            Success = true,
            Message = "AI response generated successfully.",
            Data = result
        });
    }
}
