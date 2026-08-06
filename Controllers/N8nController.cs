using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using raft_backend.DTOs;
using raft_backend.Response;

namespace raft_backend.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("n8n-management")]
[Route("api/me/n8n")]
public class N8nController : ControllerBase
{
    private readonly IN8nProvisioningService _service;

    public N8nController(IN8nProvisioningService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<IReadOnlyList<N8nAccountReadDto>>>> GetMyAccounts(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var accounts = await _service.GetAllByUserIdAsync(userId, cancellationToken);

        return Ok(new ServiceResponse<IReadOnlyList<N8nAccountReadDto>>
        {
            Success = true,
            Message = "N8N accounts retrieved successfully.",
            Data = accounts
        });
    }

    [HttpPost("provision")]
    [EnableRateLimiting("n8n-provisioning")]
    public async Task<ActionResult<ServiceResponse<N8nProvisioningResultDto>>> Provision(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _service.ProvisionAsync(userId, cancellationToken);
        if (result is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ServiceResponse<N8nProvisioningResultDto>
            {
                Success = false,
                Message = "The N8N account could not be provisioned."
            });
        }

        var response = new ServiceResponse<N8nProvisioningResultDto>
        {
            Success = true,
            Message = result.Created
                ? "N8N account provisioned successfully."
                : "You already have an active or pending N8N account.",
            Data = result
        };

        return result.Created
            ? CreatedAtAction(nameof(GetMyAccounts), null, response)
            : Ok(response);
    }

    private int GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal is missing a user id claim.");

        return int.Parse(value);
    }
}
