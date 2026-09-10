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

    // ====== ENDPOINT MONOLÍTICO (COMPATIBILIDAD) ======
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

    // ====== ENDPOINTS SEPARADOS (NUEVA ARQUITECTURA) ======

    [HttpGet("performance")]
    public async Task<IActionResult> Performance(
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
        => Ok(await _service.PerformanceAsync(period, rate, tz, refDate, from, to, granularity, sortBy, sortDir, page, limit, ct));

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(
        [FromQuery] string? period,
        [FromQuery] string? rate,
        [FromQuery] string? tz,
        [FromQuery] string? refDate,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
        => Ok(await _service.CategoriesAsync(period, rate, tz, refDate, from, to, ct));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] string? period,
        [FromQuery] string? rate,
        [FromQuery] string? tz,
        [FromQuery] string? refDate,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
        => Ok(await _service.SummaryAsync(period, rate, tz, refDate, from, to, ct));

    [HttpGet("exchanges")]
    public async Task<IActionResult> Exchanges(
        [FromQuery] string? period,
        [FromQuery] string? rate,
        [FromQuery] string? tz,
        [FromQuery] string? refDate,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
        => Ok(await _service.ExchangeStatsAsync(period, rate, tz, refDate, from, to, ct));

    [HttpGet("wallets")]
    public async Task<IActionResult> Wallets(CancellationToken ct)
        => Ok(await _service.WalletsAsync(ct));
}
