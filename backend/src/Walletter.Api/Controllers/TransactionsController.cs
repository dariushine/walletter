using Walletter.Application.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Walletter.Api.Controllers;

[ApiController]
[Route("transactions")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly TransactionsService _service;

    public TransactionsController(TransactionsService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListTransactionsQuery query, CancellationToken ct)
        => Ok(await _service.List(query, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
        => Ok(await _service.Detail(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransactionCommand cmd, CancellationToken ct)
        => Ok(await _service.CreateFull(cmd, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTransactionCommand cmd, CancellationToken ct)
        => Ok(await _service.Update(id, cmd, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
        => Ok(await _service.Remove(id, ct));

    [HttpPost("{id:int}/fee")]
    public async Task<IActionResult> AddFee(int id, [FromBody] AddFeeCommand cmd, CancellationToken ct)
        => Ok(await _service.AddFee(id, cmd, ct));

    [HttpPost("{id:int}/associate")]
    public async Task<IActionResult> Associate(int id, [FromBody] AssociateTransactionCommand cmd, CancellationToken ct)
        => Ok(await _service.Associate(id, cmd, ct));
}
