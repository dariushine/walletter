namespace Walletter.Application.Imports;

/// <summary>Importación de transacciones desde CSV (carga por lote).</summary>
public class ImportTransactionsCommand
{
    public string Csv { get; set; } = string.Empty;
    public string? Tz { get; set; }
    /// <summary>Si es true solo valida y devuelve el preview, sin escribir nada.</summary>
    public bool DryRun { get; set; }
}

/// <summary>Importación de exchanges desde CSV (carga por lote).</summary>
public class ImportExchangesCommand
{
    public string Csv { get; set; } = string.Empty;
    public string? Tz { get; set; }
    /// <summary>Si es true solo valida y devuelve el preview, sin escribir nada.</summary>
    public bool DryRun { get; set; }
}

/// <summary>Resultado por fila del CSV (1-based, sin contar el encabezado).</summary>
public record ImportRowResult(int Row, bool Ok, string? Error, object? Data = null);

/// <summary>Resultado global de una importación (preview o commit).</summary>
public record ImportResult(
    bool DryRun,
    int Total,
    int Imported,
    int Failed,
    List<ImportRowResult> Rows);
