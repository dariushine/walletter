using Walletter.Application.Recurring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Walletter.Api.Controllers;

[ApiController]
[Route("recurring-payments")]
[Authorize]
public class RecurringController : ControllerBase
{
    private readonly RecurringService _service;

    public RecurringController(RecurringService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _service.List(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
        => Ok(await _service.Detail(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecurringCommand cmd, CancellationToken ct)
        => Ok(await _service.Create(cmd, ct));

    [HttpPost("{id:int}/execute")]
    public async Task<IActionResult> Execute(int id, [FromBody] ExecuteRecurringCommand cmd, CancellationToken ct)
        => Ok(await _service.Execute(id, cmd, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateRecurringCommand cmd, CancellationToken ct)
        => Ok(await _service.Update(id, cmd, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
        => Ok(await _service.Remove(id, ct));
}
