using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using raft_backend.DTOs;
using raft_backend.Response;
using raft_backend.Services;

namespace raft_backend.Controllers;

// Self-service endpoints for the authenticated user's own databases. The user id always
// comes from the JWT claim, never from a route/query parameter, so there is no way for a
// caller to view or reveal another user's data (see UserDashboardController for the
// admin-only, userId-by-route equivalent).
[ApiController]
[Authorize]
[Route("api/me/databases")]
public class MyDatabasesController : ControllerBase
{
    private readonly IUserDashboardService _dashboardService;
    private readonly IAccessCredentialService _accessCredentialService;
    private readonly IAuditEventService _auditEventService;

    public MyDatabasesController(
        IUserDashboardService dashboardService,
        IAccessCredentialService accessCredentialService,
        IAuditEventService auditEventService)
    {
        _dashboardService = dashboardService;
        _accessCredentialService = accessCredentialService;
        _auditEventService = auditEventService;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<IEnumerable<UserDashboardDto>>>> GetMyDatabases(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var databases = await _dashboardService.GetByUserIdAsync(userId, cancellationToken);

        return Ok(new ServiceResponse<IEnumerable<UserDashboardDto>>
        {
            Success = true,
            Message = "Databases retrieved successfully.",
            Data = databases
        });
    }

    [HttpGet("{databaseInstanceId:int}/password")]
    [EnableRateLimiting("credential-reveal")]
    public async Task<ActionResult<ServiceResponse<AccessCredentialRevealDto>>> RevealPassword(int databaseInstanceId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var reveal = await _accessCredentialService.RevealPasswordAsync(userId, databaseInstanceId, cancellationToken);

        if (reveal is null)
        {
            return NotFound(new ServiceResponse<AccessCredentialRevealDto>
            {
                Success = false,
                Message = "Database instance not found or you do not have access to it."
            });
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = userId,
            EventType = "CredentialRevealed",
            Description = $"Password revealed for database instance {databaseInstanceId}."
        }, cancellationToken);

        return Ok(new ServiceResponse<AccessCredentialRevealDto>
        {
            Success = true,
            Message = "Password retrieved successfully.",
            Data = reveal
        });
    }

    private int GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal is missing a user id claim.");

        return int.Parse(value);
    }
}
