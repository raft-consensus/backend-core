using Microsoft.AspNetCore.Mvc;
using raft_backend.DTOs;
using raft_backend.Interfaces;
using raft_backend.Response;

namespace raft_backend.Controllers;

[ApiController]
[Route("api/engines")]
public class EnginesController : ControllerBase
{
    private readonly IDatabaseProvisioningServiceResolver _resolver;

    public EnginesController(IDatabaseProvisioningServiceResolver resolver)
    {
        _resolver = resolver;
    }

    [HttpGet]
    public ActionResult<ServiceResponse<IEnumerable<EngineCatalogItemDto>>> Get()
    {
        var engines = _resolver.GetAll()
            .Select(service => new EngineCatalogItemDto
            {
                Name = service.Engine,
                SupportedByThisCell = service.IsAvailable,
                Status = service.IsAvailable ? "Available" : "Unavailable",
                Notes = service.IsAvailable
                    ? $"This cell can provision and manage {service.Engine} databases."
                    : $"{service.Engine} is registered but the runtime driver or connection is not available."
            })
            .ToArray();

        if (engines.Length == 0)
        {
            engines = Array.Empty<EngineCatalogItemDto>();
        }

        return Ok(new ServiceResponse<IEnumerable<EngineCatalogItemDto>>
        {
            Success = true,
            Message = "Engine catalog retrieved successfully.",
            Data = engines
        });
    }
}
