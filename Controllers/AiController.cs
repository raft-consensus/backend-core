using System.Text.Json;
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
        var apiKey = ExtractApiKeyFromRequest();
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

    [HttpPost("/v1/chat/completions")]
    [HttpPost("/api/v1/chat/completions")]
    [HttpPost("v1/chat/completions")]
    public async Task<IActionResult> ChatCompletions(
        [FromBody] JsonElement requestPayload,
        [FromQuery] string? provider,
        CancellationToken cancellationToken)
    {
        var apiKey = ExtractApiKeyFromRequest();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unauthorized(new
            {
                error = new
                {
                    message = "Missing Authorization (Bearer) or X-API-Key header.",
                    type = "invalid_request_error",
                    code = "missing_api_key"
                }
            });
        }

        var responseJson = await _service.ProxyOpenAiChatCompletionAsync(apiKey, requestPayload, provider, cancellationToken);
        if (responseJson is null)
        {
            return Unauthorized(new
            {
                error = new
                {
                    message = "Invalid or inactive API key.",
                    type = "invalid_request_error",
                    code = "invalid_api_key"
                }
            });
        }

        return Content(responseJson.Value.GetRawText(), "application/json");
    }

    private string ExtractApiKeyFromRequest()
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
        return apiKey;
    }
}

