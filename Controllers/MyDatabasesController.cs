using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.DTOs;
using raft_backend.Interfaces;
using raft_backend.Response;
using raft_backend.Services;

namespace raft_backend.Controllers;

// Self-service endpoints for the authenticated user's own databases. The user id always
// comes from the JWT claim, never from a route/query parameter, so there is no way for a
// caller to view, reveal, or provision data for another user (see UserDashboardController
// for the admin-only, userId-by-route equivalent, and DatabaseInstancesController for the
// admin-only raw CRUD over the DatabaseInstances table).
[ApiController]
[Authorize]
[Route("api/me/databases")]
public class MyDatabasesController : ControllerBase
{
    private readonly IUserDashboardService _dashboardService;
    private readonly IAccessCredentialService _accessCredentialService;
    private readonly IAuditEventService _auditEventService;
    private readonly ISqlServerProvisioningService _provisioningService;
    private readonly SqlServerProvisioningOptions _provisioningOptions;

    public MyDatabasesController(
        IUserDashboardService dashboardService,
        IAccessCredentialService accessCredentialService,
        IAuditEventService auditEventService,
        ISqlServerProvisioningService provisioningService,
        IOptions<SqlServerProvisioningOptions> provisioningOptions)
    {
        _dashboardService = dashboardService;
        _accessCredentialService = accessCredentialService;
        _auditEventService = auditEventService;
        _provisioningService = provisioningService;
        _provisioningOptions = provisioningOptions.Value;
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

    [HttpPost]
    [EnableRateLimiting("database-provisioning")]
    public async Task<ActionResult<ServiceResponse<SqlServerProvisioningResultDto>>> CreateDatabase(
        DatabaseProvisioningRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (!string.Equals(request.Engine?.Trim(), "SQL Server", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ServiceResponse<SqlServerProvisioningResultDto>
            {
                Success = false,
                Message = "The requested engine is offered by another team and is not yet integrated."
            });
        }

        var existing = await _dashboardService.GetByUserIdAsync(userId, cancellationToken);
        if (existing.Count >= _provisioningOptions.MaxDatabasesPerUser)
        {
            return Conflict(new ServiceResponse<SqlServerProvisioningResultDto>
            {
                Success = false,
                Message = $"You already have {existing.Count} database(s). The maximum allowed per account is {_provisioningOptions.MaxDatabasesPerUser}."
            });
        }

        SqlServerProvisioningResultDto result;
        try
        {
            result = await _provisioningService.ProvisionDatabaseAsync(userId, cancellationToken);
        }
        catch (Exception)
        {
            await _auditEventService.CreateAsync(new AuditEventCreateDto
            {
                UserId = userId,
                EventType = "ProvisioningFailed",
                Description = "Self-service SQL Server database provisioning failed."
            }, cancellationToken);

            return StatusCode(StatusCodes.Status500InternalServerError, new ServiceResponse<SqlServerProvisioningResultDto>
            {
                Success = false,
                Message = "The database could not be provisioned. Please try again in a few minutes."
            });
        }

        // The password is only ever returned here, in plaintext, at creation time — it is
        // encrypted at rest afterward and can only be retrieved again via RevealPassword.
        return CreatedAtAction(nameof(GetMyDatabases), null, new ServiceResponse<SqlServerProvisioningResultDto>
        {
            Success = true,
            Message = "Database provisioned successfully. Save the password now — it will not be shown in full again.",
            Data = result
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
