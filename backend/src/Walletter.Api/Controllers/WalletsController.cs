using Walletter.Application.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Walletter.Api.Controllers;

[ApiController]
[Route("wallets")]
[Authorize]
public class WalletsController : ControllerBase
{
    private readonly WalletsService _service;

    public WalletsController(WalletsService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _service.List(ct));

    // /deleted debe declararse ANTES de /:id para no ser capturado por el param
    [HttpGet("deleted")]
    public async Task<IActionResult> ListDeleted(CancellationToken ct)
        => Ok(await _service.ListDeleted(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var wallet = await _service.Detail(id, ct);
        if (wallet == null) return NotFound(new { error = "Billetera no encontrada" });
        return Ok(wallet);
    }

    [HttpGet("{id:int}/report")]
    public async Task<IActionResult> Report(int id, CancellationToken ct)
        => Ok(await _service.Report(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWalletCommand cmd, CancellationToken ct)
        => Ok(await _service.Create(cmd, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateWalletCommand cmd, CancellationToken ct)
        => Ok(await _service.Update(id, cmd, ct));

    [HttpPut("{id:int}/reactivate")]
    public async Task<IActionResult> Reactivate(int id, CancellationToken ct)
        => Ok(await _service.Reactivate(id, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
        => Ok(await _service.Remove(id, ct));
}
