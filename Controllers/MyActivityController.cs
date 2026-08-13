using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using raft_backend.DTOs;
using raft_backend.Interfaces;
using raft_backend.Response;

namespace raft_backend.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("auth")]
[Route("api/me/activity")]
public class MyActivityController : ControllerBase
{
    private readonly IAuditEventService _auditEventService;

    public MyActivityController(IAuditEventService auditEventService)
    {
        _auditEventService = auditEventService;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<IReadOnlyList<AuditEventReadDto>>>> GetMyActivity(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        var safeLimit = Math.Clamp(limit, 1, 100);
        var activities = await _auditEventService.GetByUserIdAsync(userId, safeLimit, cancellationToken);

        return Ok(new ServiceResponse<IReadOnlyList<AuditEventReadDto>>
        {
            Success = true,
            Message = "User activity retrieved successfully.",
            Data = activities
        });
    }

    private int GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal is missing a user id claim.");

        return int.Parse(value);
    }
}
