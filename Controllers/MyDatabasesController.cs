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
    private readonly IDatabaseProvisioningServiceResolver _provisioningResolver;
    private readonly ILogger<MyDatabasesController> _logger;

    public MyDatabasesController(
        IUserDashboardService dashboardService,
        IAccessCredentialService accessCredentialService,
        IAuditEventService auditEventService,
        IDatabaseProvisioningServiceResolver provisioningResolver,
        ILogger<MyDatabasesController> logger)
    {
        _dashboardService = dashboardService;
        _accessCredentialService = accessCredentialService;
        _auditEventService = auditEventService;
        _provisioningResolver = provisioningResolver;
        _logger = logger;
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
    public async Task<ActionResult<ServiceResponse<DatabaseProvisioningResultDto>>> CreateDatabase(
        DatabaseProvisioningRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var engine = string.IsNullOrWhiteSpace(request.Engine) ? "SQL Server" : request.Engine.Trim();

        IDatabaseProvisioningService provisioningService;
        try
        {
            provisioningService = _provisioningResolver.Resolve(engine);
        }
        catch
        {
            return StatusCode(StatusCodes.Status501NotImplemented, new ServiceResponse<DatabaseProvisioningResultDto>
            {
                Success = false,
                Message = "The requested engine is not implemented by this backend."
            });
        }

        if (!provisioningService.IsAvailable)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ServiceResponse<DatabaseProvisioningResultDto>
            {
                Success = false,
                Message = $"The requested engine '{provisioningService.Engine}' is not currently available."
            });
        }

        var existing = await _dashboardService.GetByUserIdAsync(userId, cancellationToken);
        var existingForEngine = existing.Count(database => string.Equals(database.Engine, provisioningService.Engine, StringComparison.OrdinalIgnoreCase));
        if (existingForEngine >= provisioningService.MaxDatabasesPerUser)
        {
            return Conflict(new ServiceResponse<DatabaseProvisioningResultDto>
            {
                Success = false,
                Message = $"You already have {existingForEngine} {provisioningService.Engine} database(s). The maximum allowed per account for {provisioningService.Engine} is {provisioningService.MaxDatabasesPerUser}."
            });
        }

        DatabaseProvisioningResultDto result;
        try
        {
            result = await provisioningService.ProvisionDatabaseAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Self-service database provisioning failed for user {UserId} and engine {Engine}", userId, provisioningService.Engine);

            await _auditEventService.CreateAsync(new AuditEventCreateDto
            {
                UserId = userId,
                EventType = "ProvisioningFailed",
                Description = $"Self-service {provisioningService.Engine} database provisioning failed: {ex.Message}"
            }, cancellationToken);

            return StatusCode(StatusCodes.Status500InternalServerError, new ServiceResponse<DatabaseProvisioningResultDto>
            {
                Success = false,
                Message = $"The database could not be provisioned: {ex.Message}"
            });
        }

        // The password is only ever returned here, in plaintext, at creation time — it is
        // encrypted at rest afterward and can only be retrieved again via RevealPassword.
        return CreatedAtAction(nameof(GetMyDatabases), null, new ServiceResponse<DatabaseProvisioningResultDto>
        {
            Success = true,
            Message = "Database provisioned successfully. Save the password now — it will not be shown in full again.",
            Data = result
        });
    }

    [HttpPost("{databaseInstanceId:int}/pause")]
    [EnableRateLimiting("database-management")]
    public async Task<ActionResult<ServiceResponse<bool>>> PauseDatabase(int databaseInstanceId, CancellationToken cancellationToken)
    {
        var ownedDatabase = await GetOwnedDatabaseAsync(databaseInstanceId, cancellationToken);
        if (ownedDatabase is null)
        {
            return NotFound(new ServiceResponse<bool>
            {
                Success = false,
                Message = "Database instance not found or you do not have access to it."
            });
        }

        try
        {
            await _provisioningResolver.Resolve(ownedDatabase.Engine).PauseAsync(databaseInstanceId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause database instance {DatabaseInstanceId} for user {UserId}", databaseInstanceId, GetUserId());
            return StatusCode(StatusCodes.Status500InternalServerError, new ServiceResponse<bool>
            {
                Success = false,
                Message = "The database could not be paused."
            });
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = GetUserId(),
            EventType = "DatabasePaused",
            Description = $"User paused database instance {databaseInstanceId}."
        }, cancellationToken);

        return Ok(new ServiceResponse<bool>
        {
            Success = true,
            Message = "Database paused successfully.",
            Data = true
        });
    }

    [HttpPost("{databaseInstanceId:int}/resume")]
    [EnableRateLimiting("database-management")]
    public async Task<ActionResult<ServiceResponse<bool>>> ResumeDatabase(int databaseInstanceId, CancellationToken cancellationToken)
    {
        var ownedDatabase = await GetOwnedDatabaseAsync(databaseInstanceId, cancellationToken);
        if (ownedDatabase is null)
        {
            return NotFound(new ServiceResponse<bool>
            {
                Success = false,
                Message = "Database instance not found or you do not have access to it."
            });
        }

        try
        {
            await _provisioningResolver.Resolve(ownedDatabase.Engine).ResumeAsync(databaseInstanceId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume database instance {DatabaseInstanceId} for user {UserId}", databaseInstanceId, GetUserId());
            return StatusCode(StatusCodes.Status500InternalServerError, new ServiceResponse<bool>
            {
                Success = false,
                Message = "The database could not be resumed."
            });
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = GetUserId(),
            EventType = "DatabaseResumed",
            Description = $"User resumed database instance {databaseInstanceId}."
        }, cancellationToken);

        return Ok(new ServiceResponse<bool>
        {
            Success = true,
            Message = "Database resumed successfully.",
            Data = true
        });
    }

    [HttpDelete("{databaseInstanceId:int}")]
    [EnableRateLimiting("database-management")]
    public async Task<ActionResult<ServiceResponse<bool>>> DeleteDatabase(int databaseInstanceId, CancellationToken cancellationToken)
    {
        var ownedDatabase = await GetOwnedDatabaseAsync(databaseInstanceId, cancellationToken);
        if (ownedDatabase is null)
        {
            return NotFound(new ServiceResponse<bool>
            {
                Success = false,
                Message = "Database instance not found or you do not have access to it."
            });
        }

        try
        {
            await _provisioningResolver.Resolve(ownedDatabase.Engine).DeleteAsync(databaseInstanceId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete database instance {DatabaseInstanceId} for user {UserId}", databaseInstanceId, GetUserId());
            return StatusCode(StatusCodes.Status500InternalServerError, new ServiceResponse<bool>
            {
                Success = false,
                Message = "The database could not be deleted."
            });
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = GetUserId(),
            EventType = "DatabaseDeleted",
            Description = $"User deleted database instance {databaseInstanceId}."
        }, cancellationToken);

        return Ok(new ServiceResponse<bool>
        {
            Success = true,
            Message = "Database deleted successfully.",
            Data = true
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

    private async Task<UserDashboardDto?> GetOwnedDatabaseAsync(int databaseInstanceId, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var databases = await _dashboardService.GetByUserIdAsync(userId, cancellationToken);
        return databases.FirstOrDefault(database => database.DatabaseInstanceId == databaseInstanceId);
    }
}
