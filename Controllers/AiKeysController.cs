using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using raft_backend.DTOs;
using raft_backend.Response;

namespace raft_backend.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("ai-key-management")]
[Route("api/me/ai-keys")]
public class AiKeysController : ControllerBase
{
    private readonly IAiApiKeyService _service;

    public AiKeysController(IAiApiKeyService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<IEnumerable<AiApiKeyReadDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var items = await _service.GetAllByUserIdAsync(userId, cancellationToken);

        return Ok(new ServiceResponse<IEnumerable<AiApiKeyReadDto>>
        {
            Success = true,
            Message = "AI API keys retrieved successfully.",
            Data = items
        });
    }

    [HttpPost]
    public async Task<ActionResult<ServiceResponse<AiApiKeySecretDto>>> Create(AiApiKeyCreateDto dto, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var item = await _service.CreateAsync(userId, dto, cancellationToken);
        if (item is null)
        {
            return BadRequest(new ServiceResponse<AiApiKeySecretDto>
            {
                Success = false,
                Message = "The AI API key could not be created."
            });
        }

        return CreatedAtAction(nameof(GetAll), routeValues: null, value: new ServiceResponse<AiApiKeySecretDto>
        {
            Success = true,
            Message = "AI API key created successfully. Save the secret now; it will not be shown again.",
            Data = item
        });
    }

    [HttpPost("{id:int}/rotate")]
    public async Task<ActionResult<ServiceResponse<AiApiKeySecretDto>>> Rotate(int id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var item = await _service.RotateAsync(userId, id, cancellationToken);
        if (item is null)
        {
            return NotFound(new ServiceResponse<AiApiKeySecretDto>
            {
                Success = false,
                Message = "AI API key not found."
            });
        }

        return Ok(new ServiceResponse<AiApiKeySecretDto>
        {
            Success = true,
            Message = "AI API key rotated successfully. Save the new secret now; it will not be shown again.",
            Data = item
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ServiceResponse<bool>>> Revoke(int id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var deleted = await _service.RevokeAsync(userId, id, cancellationToken);
        if (!deleted)
        {
            return NotFound(new ServiceResponse<bool>
            {
                Success = false,
                Message = "AI API key not found."
            });
        }

        return Ok(new ServiceResponse<bool>
        {
            Success = true,
            Message = "AI API key revoked successfully.",
            Data = true
        });
    }

    [HttpGet("usage/history")]
    public async Task<ActionResult<ServiceResponse<IEnumerable<AiUsageLogReadDto>>>> GetHistory(
        [FromQuery] AiUsageHistoryFilterDto filter,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var items = await _service.GetUsageHistoryAsync(userId, filter, cancellationToken);

        return Ok(new ServiceResponse<IEnumerable<AiUsageLogReadDto>>
        {
            Success = true,
            Message = "AI usage history retrieved successfully.",
            Data = items
        });
    }

    [HttpGet("usage/analytics")]
    public async Task<ActionResult<ServiceResponse<AiUsageAnalyticsDto>>> GetAnalytics(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var data = await _service.GetUsageAnalyticsAsync(userId, fromDate, toDate, cancellationToken);

        return Ok(new ServiceResponse<AiUsageAnalyticsDto>
        {
            Success = true,
            Message = "AI usage analytics retrieved successfully.",
            Data = data
        });
    }

    private int GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal is missing a user id claim.");

        return int.Parse(value);
    }
}
