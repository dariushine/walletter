using Walletter.Application.Pending;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Walletter.Api.Controllers;

[ApiController]
[Route("pending-payments")]
[Authorize]
public class PendingPaymentsController : ControllerBase
{
    private readonly PendingPaymentsService _service;

    public PendingPaymentsController(PendingPaymentsService service)
    {
        _service = service;
    }

    /// <summary>Lista los pendientes no cancelados. ?includePaid=true incluye los pagados.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includePaid = false, CancellationToken ct = default)
        => Ok(await _service.List(includePaid, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
        => Ok(await _service.Detail(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePendingPaymentCommand cmd, CancellationToken ct)
        => Ok(await _service.Create(cmd, ct));

    [HttpPost("{id:int}/pay")]
    public async Task<IActionResult> Pay(int id, [FromBody] PayPendingPaymentCommand cmd, CancellationToken ct)
        => Ok(await _service.Pay(id, cmd, ct));

    [HttpPost("{id:int}/mark-paid")]
    public async Task<IActionResult> MarkPaid(int id, [FromBody] MarkPendingPaidCommand cmd, CancellationToken ct)
        => Ok(await _service.MarkPaid(id, cmd, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePendingPaymentCommand cmd, CancellationToken ct)
        => Ok(await _service.Update(id, cmd, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
        => Ok(await _service.Remove(id, ct));
}
