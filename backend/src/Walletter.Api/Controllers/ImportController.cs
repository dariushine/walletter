using Walletter.Application.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Walletter.Api.Controllers;

/// <summary>
/// Carga por lote (CSV) de transacciones y exchanges. El front envía el CSV
/// como texto; cada fila se registra con la misma lógica que la creación
/// individual. Con dryRun=true solo valida y devuelve el preview.
/// </summary>
[ApiController]
[Route("import")]
[Authorize]
public class ImportController : ControllerBase
{
    private readonly ImportService _service;

    public ImportController(ImportService service)
    {
        _service = service;
    }

    [HttpPost("transactions")]
    public async Task<IActionResult> Transactions([FromBody] ImportTransactionsCommand cmd, CancellationToken ct)
        => Ok(await _service.ImportTransactions(cmd, ct));

    [HttpPost("exchanges")]
    public async Task<IActionResult> Exchanges([FromBody] ImportExchangesCommand cmd, CancellationToken ct)
        => Ok(await _service.ImportExchanges(cmd, ct));
}
