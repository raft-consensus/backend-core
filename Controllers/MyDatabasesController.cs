using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using raft_backend.Configuration;
using raft_backend.DTOs;
using raft_backend.Interfaces;
using raft_backend.Response;

namespace raft_backend.Controllers;

[ApiController]
[Authorize]
[Route("api/me/databases")]
public class MyDatabasesController : ControllerBase
{
    private readonly IUserDashboardService _dashboardService;
    private readonly IAccessCredentialService _accessCredentialService;
    private readonly IAuditEventService _auditEventService;
    private readonly IEnumerable<IEngineProvisioningService> _provisioningServices;
    private readonly SqlServerProvisioningOptions _provisioningOptions;

    public MyDatabasesController(
        IUserDashboardService dashboardService,
        IAccessCredentialService accessCredentialService,
        IAuditEventService auditEventService,
        IEnumerable<IEngineProvisioningService> provisioningServices,
        IOptions<SqlServerProvisioningOptions> provisioningOptions)
    {
        _dashboardService = dashboardService;
        _accessCredentialService = accessCredentialService;
        _auditEventService = auditEventService;
        _provisioningServices = provisioningServices;
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
        var requestedEngine = string.IsNullOrWhiteSpace(request.Engine) ? "SQL Server" : request.Engine.Trim();

        var service = _provisioningServices.FirstOrDefault(s =>
            string.Equals(s.EngineName, requestedEngine, StringComparison.OrdinalIgnoreCase));

        if (service is null)
        {
            return BadRequest(new ServiceResponse<SqlServerProvisioningResultDto>
            {
                Success = false,
                Message = $"Engine '{requestedEngine}' is not supported. Supported engines are: {string.Join(", ", _provisioningServices.Select(s => s.EngineName))}."
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
            result = await service.ProvisionAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            await _auditEventService.CreateAsync(new AuditEventCreateDto
            {
                UserId = userId,
                EventType = "ProvisioningFailed",
                Description = $"Self-service {requestedEngine} database provisioning failed: {ex.Message}"
            }, cancellationToken);

            return StatusCode(StatusCodes.Status500InternalServerError, new ServiceResponse<SqlServerProvisioningResultDto>
            {
                Success = false,
                Message = $"The {requestedEngine} database could not be provisioned. Please try again in a few minutes."
            });
        }

        return CreatedAtAction(nameof(GetMyDatabases), null, new ServiceResponse<SqlServerProvisioningResultDto>
        {
            Success = true,
            Message = $"{requestedEngine} database provisioned successfully. Save the password now — it will not be shown in full again.",
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
