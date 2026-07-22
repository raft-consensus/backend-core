using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using raft_backend.DTOs;
using raft_backend.Response;
using raft_backend.Services;

namespace raft_backend.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/audit-events")]
public class AuditEventsController : ControllerBase
{
    private readonly IAuditEventService _service;

    public AuditEventsController(IAuditEventService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<IEnumerable<AuditEventReadDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var items = await _service.GetAllAsync(cancellationToken);
        return Ok(new ServiceResponse<IEnumerable<AuditEventReadDto>>
        {
            Success = true,
            Message = "Audit events retrieved successfully.",
            Data = items
        });
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ServiceResponse<AuditEventReadDto>>> GetById(long id, CancellationToken cancellationToken)
    {
        var item = await _service.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound(new ServiceResponse<AuditEventReadDto>
            {
                Success = false,
                Message = "Audit event not found."
            });
        }

        return Ok(new ServiceResponse<AuditEventReadDto>
        {
            Success = true,
            Message = "Audit event retrieved successfully.",
            Data = item
        });
    }

    [HttpPost]
    public async Task<ActionResult<ServiceResponse<AuditEventReadDto>>> Create(AuditEventCreateDto dto, CancellationToken cancellationToken)
    {
        var item = await _service.CreateAsync(dto, cancellationToken);
        if (item is null)
        {
            return BadRequest(new ServiceResponse<AuditEventReadDto>
            {
                Success = false,
                Message = "The audit event could not be created."
            });
        }

        return CreatedAtAction(nameof(GetById), new { id = item.Id }, new ServiceResponse<AuditEventReadDto>
        {
            Success = true,
            Message = "Audit event created successfully.",
            Data = item
        });
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ServiceResponse<AuditEventReadDto>>> Update(long id, AuditEventUpdateDto dto, CancellationToken cancellationToken)
    {
        var item = await _service.UpdateAsync(id, dto, cancellationToken);
        if (item is null)
        {
            return NotFound(new ServiceResponse<AuditEventReadDto>
            {
                Success = false,
                Message = "Audit event not found or could not be updated."
            });
        }

        return Ok(new ServiceResponse<AuditEventReadDto>
        {
            Success = true,
            Message = "Audit event updated successfully.",
            Data = item
        });
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ServiceResponse<bool>>> Delete(long id, CancellationToken cancellationToken)
    {
        var deleted = await _service.SoftDeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            return NotFound(new ServiceResponse<bool>
            {
                Success = false,
                Message = "Audit event not found."
            });
        }

        return Ok(new ServiceResponse<bool>
        {
            Success = true,
            Message = "Audit event deleted successfully.",
            Data = true
        });
    }
}
