using Walletter.Application.Exchanges;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Walletter.Api.Controllers;

[ApiController]
[Route("exchanges")]
[Authorize]
public class ExchangesController : ControllerBase
{
    private readonly ExchangesService _service;

    public ExchangesController(ExchangesService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListExchangesQuery query, CancellationToken ct)
        => Ok(await _service.List(query, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
        => Ok(await _service.Detail(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateExchangeCommand cmd, CancellationToken ct)
        => Ok(await _service.Create(cmd, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateExchangeCommand cmd, CancellationToken ct)
        => Ok(await _service.Update(id, cmd, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
        => Ok(await _service.Remove(id, ct));
}
