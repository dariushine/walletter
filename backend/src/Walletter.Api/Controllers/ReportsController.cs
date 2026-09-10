using Walletter.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Walletter.Api.Controllers;

[ApiController]
[Route("reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly ReportsService _service;

    public ReportsController(ReportsService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Overview(
        [FromQuery] string? period,
        [FromQuery] string? rate,
        [FromQuery] string? tz,
        [FromQuery] string? refDate,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? granularity,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        [FromQuery] int? page,
        [FromQuery] int? limit,
        CancellationToken ct)
        => Ok(await _service.Overview(period, rate, tz, refDate, from, to, granularity, sortBy, sortDir, page, limit, ct));
}
