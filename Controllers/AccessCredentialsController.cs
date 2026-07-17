using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using raft_backend.DTOs;
using raft_backend.Response;
using raft_backend.Services;

namespace raft_backend.Controllers;

[ApiController]
[Authorize]
[Route("api/access-credentials")]
public class AccessCredentialsController : ControllerBase
{
    private readonly IAccessCredentialService _service;

    public AccessCredentialsController(IAccessCredentialService service)
    {
        _service = service;
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

        return Ok(new ServiceResponse<bool>
        {
            Success = true,
            Message = "Access credential deleted successfully.",
            Data = true
        });
    }
}
