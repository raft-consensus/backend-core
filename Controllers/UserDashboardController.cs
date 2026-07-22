using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using raft_backend.DTOs;
using raft_backend.Response;
using raft_backend.Services;

namespace raft_backend.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/users/{userId:int}/dashboard")]
public class UserDashboardController : ControllerBase
{
    private readonly IUserDashboardService _service;

    public UserDashboardController(IUserDashboardService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<IEnumerable<UserDashboardDto>>>> GetByUserId(int userId, CancellationToken cancellationToken)
    {
        var dashboard = await _service.GetByUserIdAsync(userId, cancellationToken);
        if (dashboard.Count == 0)
        {
            return NotFound(new ServiceResponse<IEnumerable<UserDashboardDto>>
            {
                Success = false,
                Message = "User not found or no dashboard data available."
            });
        }

        return Ok(new ServiceResponse<IEnumerable<UserDashboardDto>>
        {
            Success = true,
            Message = "Dashboard retrieved successfully.",
            Data = dashboard
        });
    }
}
