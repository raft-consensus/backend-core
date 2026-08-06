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
[EnableRateLimiting("dns-management")]
[Route("api/me/dns")]
public class DnsController : ControllerBase
{
    private readonly IDnsProvisioningService _service;

    public DnsController(IDnsProvisioningService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<IReadOnlyList<DnsRecordReadDto>>>> GetMyRecords(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var records = await _service.GetAllByUserIdAsync(userId, cancellationToken);

        return Ok(new ServiceResponse<IReadOnlyList<DnsRecordReadDto>>
        {
            Success = true,
            Message = "DNS records retrieved successfully.",
            Data = records
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceResponse<DnsRecordReadDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var record = await _service.GetByIdForUserAsync(userId, id, cancellationToken);
        if (record is null)
        {
            return NotFound(new ServiceResponse<DnsRecordReadDto>
            {
                Success = false,
                Message = "DNS record not found."
            });
        }

        return Ok(new ServiceResponse<DnsRecordReadDto>
        {
            Success = true,
            Message = "DNS record retrieved successfully.",
            Data = record
        });
    }

    [HttpPost("provision")]
    [EnableRateLimiting("dns-provisioning")]
    public async Task<ActionResult<ServiceResponse<DnsProvisioningResultDto>>> Provision(DnsRecordCreateDto dto, CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        DnsProvisioningResultDto? result;
        try
        {
            result = await _service.ProvisionAsync(userId, dto, cancellationToken);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ServiceResponse<DnsProvisioningResultDto>
            {
                Success = false,
                Message = $"The DNS record could not be provisioned: {ex.Message}"
            });
        }

        if (result is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ServiceResponse<DnsProvisioningResultDto>
            {
                Success = false,
                Message = "The DNS record could not be provisioned."
            });
        }

        return result.Created
            ? CreatedAtAction(nameof(GetById), new { id = result.Record.Id }, new ServiceResponse<DnsProvisioningResultDto>
            {
                Success = true,
                Message = "DNS record provisioned successfully.",
                Data = result
            })
            : Ok(new ServiceResponse<DnsProvisioningResultDto>
            {
                Success = true,
                Message = "You already have an active DNS record for that hostname.",
                Data = result
            });
    }

    [HttpDelete("{id:int}")]
    [EnableRateLimiting("dns-management")]
    public async Task<ActionResult<ServiceResponse<bool>>> Revoke(int id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var revoked = await _service.RevokeAsync(userId, id, cancellationToken);
        if (!revoked)
        {
            return NotFound(new ServiceResponse<bool>
            {
                Success = false,
                Message = "DNS record not found or already revoked."
            });
        }

        return Ok(new ServiceResponse<bool>
        {
            Success = true,
            Message = "DNS record revoked successfully.",
            Data = true
        });
    }

    private int GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal is missing a user id claim.");

        return int.Parse(value);
    }
}
