using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using raft_backend.DTOs;
using raft_backend.Response;

namespace raft_backend.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("admin-ops")]
[Route("api/n8n/accounts")]
public class N8nAccountsController : ControllerBase
{
    private readonly IN8nProvisioningService _service;
    private readonly IAuditEventService _auditEventService;

    public N8nAccountsController(IN8nProvisioningService service, IAuditEventService auditEventService)
    {
        _service = service;
        _auditEventService = auditEventService;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<IReadOnlyList<N8nAccountReadDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var items = await _service.GetAllAsync(cancellationToken);

        return Ok(new ServiceResponse<IReadOnlyList<N8nAccountReadDto>>
        {
            Success = true,
            Message = "N8N accounts retrieved successfully.",
            Data = items
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceResponse<N8nAccountReadDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var item = await _service.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound(new ServiceResponse<N8nAccountReadDto>
            {
                Success = false,
                Message = "N8N account not found."
            });
        }

        return Ok(new ServiceResponse<N8nAccountReadDto>
        {
            Success = true,
            Message = "N8N account retrieved successfully.",
            Data = item
        });
    }

    [HttpPost("{id:int}/revoke")]
    public async Task<ActionResult<ServiceResponse<bool>>> Revoke(int id, CancellationToken cancellationToken)
    {
        var revoked = await _service.RevokeAsync(id, cancellationToken);
        if (!revoked)
        {
            return NotFound(new ServiceResponse<bool>
            {
                Success = false,
                Message = "N8N account not found or already revoked."
            });
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = GetActorUserId(),
            EventType = "AdminN8nAccountRevoked",
            Description = $"Admin revoked N8N account {id}.",
            AdditionalData = $"{{\"n8nAccountId\":{id}}}"
        }, cancellationToken);

        return Ok(new ServiceResponse<bool>
        {
            Success = true,
            Message = "N8N account revoked successfully.",
            Data = true
        });
    }

    private int GetActorUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal is missing a user id claim.");

        return int.Parse(value);
    }
}
