using Walletter.Application.Stats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Walletter.Api.Controllers;

[ApiController]
[Route("stats")]
[Authorize]
public class StatsController : ControllerBase
{
    private readonly StatsService _service;

    public StatsController(StatsService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Overview(CancellationToken ct)
        => Ok(await _service.Overview(ct));

    [HttpGet("by-category")]
    public async Task<IActionResult> ByCategory(CancellationToken ct)
        => Ok(await _service.ByCategory(ct));
}
