using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using raft_backend.DTOs;
using raft_backend.Response;
using raft_backend.Services;

namespace raft_backend.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/database-instances")]
public class DatabaseInstancesController : ControllerBase
{
    private readonly IDatabaseInstanceService _service;

    public DatabaseInstancesController(IDatabaseInstanceService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<IEnumerable<DatabaseInstanceReadDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var items = await _service.GetAllAsync(cancellationToken);
        return Ok(new ServiceResponse<IEnumerable<DatabaseInstanceReadDto>>
        {
            Success = true,
            Message = "Database instances retrieved successfully.",
            Data = items
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceResponse<DatabaseInstanceReadDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var item = await _service.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound(new ServiceResponse<DatabaseInstanceReadDto>
            {
                Success = false,
                Message = "Database instance not found."
            });
        }

        return Ok(new ServiceResponse<DatabaseInstanceReadDto>
        {
            Success = true,
            Message = "Database instance retrieved successfully.",
            Data = item
        });
    }

    [HttpPost]
    public async Task<ActionResult<ServiceResponse<DatabaseInstanceReadDto>>> Create(DatabaseInstanceCreateDto dto, CancellationToken cancellationToken)
    {
        var item = await _service.CreateAsync(dto, cancellationToken);
        if (item is null)
        {
            return BadRequest(new ServiceResponse<DatabaseInstanceReadDto>
            {
                Success = false,
                Message = "The database instance could not be created."
            });
        }

        return CreatedAtAction(nameof(GetById), new { id = item.Id }, new ServiceResponse<DatabaseInstanceReadDto>
        {
            Success = true,
            Message = "Database instance created successfully.",
            Data = item
        });
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ServiceResponse<DatabaseInstanceReadDto>>> Update(int id, DatabaseInstanceUpdateDto dto, CancellationToken cancellationToken)
    {
        var item = await _service.UpdateAsync(id, dto, cancellationToken);
        if (item is null)
        {
            return NotFound(new ServiceResponse<DatabaseInstanceReadDto>
            {
                Success = false,
                Message = "Database instance not found or could not be updated."
            });
        }

        return Ok(new ServiceResponse<DatabaseInstanceReadDto>
        {
            Success = true,
            Message = "Database instance updated successfully.",
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
                Message = "Database instance not found."
            });
        }

        return Ok(new ServiceResponse<bool>
        {
            Success = true,
            Message = "Database instance deleted successfully.",
            Data = true
        });
    }
}
