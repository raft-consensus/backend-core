using Microsoft.AspNetCore.Mvc;
using raft_backend.DTOs;
using raft_backend.Response;
using raft_backend.Services;

namespace raft_backend.Controllers;

[ApiController]
[Route("api/metrics")]
public class PlatformMetricsController : ControllerBase
{
    private readonly IPlatformMetricsService _service;

    public PlatformMetricsController(IPlatformMetricsService service)
    {
        _service = service;
    }

    [HttpGet("platform")]
    public async Task<ActionResult<ServiceResponse<PlatformMetricsDto>>> GetPlatformMetrics(CancellationToken cancellationToken)
    {
        var metrics = await _service.GetPlatformMetricsAsync(cancellationToken);
        return Ok(new ServiceResponse<PlatformMetricsDto>
        {
            Success = true,
            Message = "Platform metrics retrieved successfully.",
            Data = metrics
        });
    }
}
