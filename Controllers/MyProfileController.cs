using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using raft_backend.DTOs;
using raft_backend.Interfaces;
using raft_backend.Response;

namespace raft_backend.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("auth")]
[Route("api/me/profile")]
public class MyProfileController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IAuditEventService _auditEventService;

    public MyProfileController(IUserService userService, IAuditEventService auditEventService)
    {
        _userService = userService;
        _auditEventService = auditEventService;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<UserReadDto>>> GetProfile(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var user = await _userService.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return NotFound(new ServiceResponse<UserReadDto>
            {
                Success = false,
                Message = "User profile not found."
            });
        }

        return Ok(new ServiceResponse<UserReadDto>
        {
            Success = true,
            Message = "User profile retrieved successfully.",
            Data = user
        });
    }

    [HttpPut]
    public async Task<ActionResult<ServiceResponse<UserReadDto>>> UpdateProfile(UserProfileUpdateDto dto, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var updated = await _userService.UpdateSelfAsync(userId, dto, cancellationToken);
        if (updated is null)
        {
            return NotFound(new ServiceResponse<UserReadDto>
            {
                Success = false,
                Message = "User profile could not be updated."
            });
        }

        await _auditEventService.CreateAsync(new AuditEventCreateDto
        {
            UserId = userId,
            EventType = "UserProfileUpdated",
            Description = $"User updated profile: Name={updated.Name}, Organization={updated.Organization}"
        }, cancellationToken);

        return Ok(new ServiceResponse<UserReadDto>
        {
            Success = true,
            Message = "User profile updated successfully.",
            Data = updated
        });
    }

    private int GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal is missing a user id claim.");

        return int.Parse(value);
    }
}
