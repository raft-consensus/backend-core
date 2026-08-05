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
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _service;
    private readonly IAuditEventService _auditEventService;

    public UsersController(IUserService service, IAuditEventService auditEventService)
    {
        _service = service;
        _auditEventService = auditEventService;
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

        await RecordAuditAsync(
            "AdminUserCreated",
            $"Admin created user {user.Id}.",
            new { targetUserId = user.Id },
            cancellationToken);

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

        await RecordAuditAsync(
            "AdminUserUpdated",
            $"Admin updated user {id}.",
            new { targetUserId = id },
            cancellationToken);

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

        await RecordAuditAsync(
            "AdminUserDeleted",
            $"Admin deleted user {id}.",
            new { targetUserId = id },
            cancellationToken);

        return Ok(new ServiceResponse<bool>
        {
            Success = true,
            Message = "User deleted successfully.",
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
