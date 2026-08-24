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
        CancellationToken ct)
        => Ok(await _service.Overview(period, rate, tz, ct));
}
