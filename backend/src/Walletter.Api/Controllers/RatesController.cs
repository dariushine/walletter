using Walletter.Application.Rates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Walletter.Api.Controllers;

[ApiController]
[Authorize]
public class RatesController : ControllerBase
{
    private readonly RatesService _service;

    public RatesController(RatesService service)
    {
        _service = service;
    }

    [HttpGet("rates/effective")]
    public async Task<IActionResult> Effective([FromQuery] string? date, CancellationToken ct)
    {
        var eff = await _service.Effective(date, ct);
        return Ok(new
        {
            date = eff.Date,
            vps = new { bcv = eff.Bcv, paralelo = eff.Paralelo },
            note = eff.Note,
        });
    }

    [HttpGet("daily-rates/today")]
    public async Task<IActionResult> Today(CancellationToken ct)
    {
        var eff = await _service.Effective(null, ct);
        // El front hace const { data } = await res.json() y usa data.bcv/paralelo.
        return Ok(new { data = new { bcv = eff.Bcv, paralelo = eff.Paralelo, date = eff.Date, source = "dolarapi" } });
    }

    [HttpGet("daily-rates")]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _service.List(ct));

    [HttpPost("daily-rates")]
    public async Task<IActionResult> Upsert([FromBody] UpsertDailyRateCommand cmd, CancellationToken ct)
        => Ok(await _service.Upsert(cmd, ct));

    [HttpGet("daily-rates/{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Ok(await _service.GetById(id, ct));

    [HttpPut("daily-rates/{id:int}")]
    public async Task<IActionResult> UpdateById(int id, [FromBody] UpdateDailyRateCommand cmd, CancellationToken ct)
        => Ok(await _service.UpdateById(id, cmd, ct));

    [HttpDelete("daily-rates/{id:int}")]
    public async Task<IActionResult> DeleteById(int id, CancellationToken ct)
        => Ok(await _service.DeleteById(id, ct));
}
