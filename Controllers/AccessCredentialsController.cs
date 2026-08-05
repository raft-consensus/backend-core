using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using raft_backend.DTOs;
using raft_backend.Response;
using raft_backend.Services;

namespace raft_backend.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("admin-ops")]
[Route("api/access-credentials")]
public class AccessCredentialsController : ControllerBase
{
    private readonly IAccessCredentialService _service;
    private readonly IAuditEventService _auditEventService;

    public AccessCredentialsController(IAccessCredentialService service, IAuditEventService auditEventService)
    {
        _service = service;
        _auditEventService = auditEventService;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<IEnumerable<AccessCredentialReadDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var items = await _service.GetAllAsync(cancellationToken);
        return Ok(new ServiceResponse<IEnumerable<AccessCredentialReadDto>>
        {
            Success = true,
            Message = "Access credentials retrieved successfully.",
            Data = items
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceResponse<AccessCredentialReadDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var item = await _service.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound(new ServiceResponse<AccessCredentialReadDto>
            {
                Success = false,
                Message = "Access credential not found."
            });
        }

        return Ok(new ServiceResponse<AccessCredentialReadDto>
        {
            Success = true,
            Message = "Access credential retrieved successfully.",
            Data = item
        });
    }

    [HttpGet("by-database-instance/{databaseInstanceId:int}")]
    public async Task<ActionResult<ServiceResponse<AccessCredentialReadDto>>> GetByDatabaseInstanceId(int databaseInstanceId, CancellationToken cancellationToken)
    {
        var item = await _service.GetByDatabaseInstanceIdAsync(databaseInstanceId, cancellationToken);
        if (item is null)
        {
            return NotFound(new ServiceResponse<AccessCredentialReadDto>
            {
                Success = false,
                Message = "Access credential not found for the specified database instance."
            });
        }

        return Ok(new ServiceResponse<AccessCredentialReadDto>
        {
            Success = true,
            Message = "Access credential retrieved successfully.",
            Data = item
        });
    }

    [HttpPost]
    public async Task<ActionResult<ServiceResponse<AccessCredentialReadDto>>> Create(AccessCredentialCreateDto dto, CancellationToken cancellationToken)
    {
        var item = await _service.CreateAsync(dto, cancellationToken);
        if (item is null)
        {
            return Conflict(new ServiceResponse<AccessCredentialReadDto>
            {
                Success = false,
                Message = "The access credential could not be created."
            });
        }

        await RecordAuditAsync(
            "AdminAccessCredentialCreated",
            $"Admin created access credential {item.Id}.",
            new { targetAccessCredentialId = item.Id, targetDatabaseInstanceId = item.DatabaseInstanceId },
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = item.Id }, new ServiceResponse<AccessCredentialReadDto>
        {
            Success = true,
            Message = "Access credential created successfully.",
            Data = item
        });
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ServiceResponse<AccessCredentialReadDto>>> Update(int id, AccessCredentialUpdateDto dto, CancellationToken cancellationToken)
    {
        var item = await _service.UpdateAsync(id, dto, cancellationToken);
        if (item is null)
        {
            return NotFound(new ServiceResponse<AccessCredentialReadDto>
            {
                Success = false,
                Message = "Access credential not found or could not be updated."
            });
        }

        await RecordAuditAsync(
            "AdminAccessCredentialUpdated",
            $"Admin updated access credential {id}.",
            new { targetAccessCredentialId = id },
            cancellationToken);

        return Ok(new ServiceResponse<AccessCredentialReadDto>
        {
            Success = true,
            Message = "Access credential updated successfully.",
            Data = item
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ServiceResponse<bool>>> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await _service.SoftDeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound(new ServiceResponse<bool>
            {
                Success = false,
                Message = "Access credential not found."
            });
        }

        await RecordAuditAsync(
            "AdminAccessCredentialDeleted",
            $"Admin deleted access credential {id}.",
            new { targetAccessCredentialId = id },
            cancellationToken);

        return Ok(new ServiceResponse<bool>
        {
            Success = true,
            Message = "Access credential deleted successfully.",
            Data = true
        });
    }

    private Task RecordAuditAsync(string eventType, string description, object? additionalData, CancellationToken cancellationToken)
    {
        return _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = GetActorUserId(),
            EventType = eventType,
            Description = description,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            AdditionalData = additionalData is null ? null : JsonSerializer.Serialize(additionalData)
        }, cancellationToken);
    }

    private int GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal is missing a user id claim.");

        return int.Parse(value);
    }
}
