using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using raft_backend.DTOs;
using raft_backend.Response;
using raft_backend.Services;

namespace raft_backend.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _service;

    public UsersController(IUserService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<IEnumerable<UserReadDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var users = await _service.GetAllAsync(cancellationToken);
        return Ok(new ServiceResponse<IEnumerable<UserReadDto>>
        {
            Success = true,
            Message = "Users retrieved successfully.",
            Data = users
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceResponse<UserReadDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var user = await _service.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound(new ServiceResponse<UserReadDto>
            {
                Success = false,
                Message = "User not found."
            });
        }

        return Ok(new ServiceResponse<UserReadDto>
        {
            Success = true,
            Message = "User retrieved successfully.",
            Data = user
        });
    }

    [HttpPost]
    public async Task<ActionResult<ServiceResponse<UserReadDto>>> Create(UserCreateDto dto, CancellationToken cancellationToken)
    {
        var user = await _service.CreateAsync(dto, cancellationToken);
        if (user is null)
        {
            return Conflict(new ServiceResponse<UserReadDto>
            {
                Success = false,
                Message = "The user could not be created."
            });
        }

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, new ServiceResponse<UserReadDto>
        {
            Success = true,
            Message = "User created successfully.",
            Data = user
        });
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ServiceResponse<UserReadDto>>> Update(int id, UserUpdateDto dto, CancellationToken cancellationToken)
    {
        var user = await _service.UpdateAsync(id, dto, cancellationToken);
        if (user is null)
        {
            return NotFound(new ServiceResponse<UserReadDto>
            {
                Success = false,
                Message = "User not found or could not be updated."
            });
        }

        return Ok(new ServiceResponse<UserReadDto>
        {
            Success = true,
            Message = "User updated successfully.",
            Data = user
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
                Message = "User not found."
            });
        }

        return Ok(new ServiceResponse<bool>
        {
            Success = true,
            Message = "User deleted successfully.",
            Data = true
        });
    }
}
