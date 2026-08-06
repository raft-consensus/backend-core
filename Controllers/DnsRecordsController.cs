using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using raft_backend.DTOs;
using raft_backend.Interfaces;
using raft_backend.Response;

namespace raft_backend.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("admin-ops")]
[Route("api/dns/records")]
public class DnsRecordsController : ControllerBase
{
    private readonly IDnsProvisioningService _service;

    public DnsRecordsController(IDnsProvisioningService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ServiceResponse<IReadOnlyList<DnsRecordReadDto>>>> GetAll(CancellationToken cancellationToken)
    {
        var items = await _service.GetAllAsync(cancellationToken);

        return Ok(new ServiceResponse<IReadOnlyList<DnsRecordReadDto>>
        {
            Success = true,
            Message = "DNS records retrieved successfully.",
            Data = items
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceResponse<DnsRecordReadDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var item = await _service.GetByIdAsync(id, cancellationToken);
        if (item is null)
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
}
