namespace Walletter.Application.Pending;

public class CreatePendingPaymentCommand
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public decimal Fee { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public int? CategoryId { get; set; }
    public int? WalletId { get; set; }

    /// <summary>Fecha límite (yyyy-MM-dd) en la zona del usuario. Opcional.</summary>
    public string? DueDate { get; set; }
}

public class UpdatePendingPaymentCommand
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Fee { get; set; }
    public string? Currency { get; set; }
    public string? Type { get; set; }
    public string? CategoryName { get; set; }
    public int? WalletId { get; set; }
    public string? DueDate { get; set; }
}

public class PayPendingPaymentCommand
{
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string? Tz { get; set; }
    public int? WalletId { get; set; }
    public decimal? OverrideAmount { get; set; }
    public decimal? OverrideFee { get; set; }
    public string? OverrideCategoryName { get; set; }
    public int? OverrideWalletId { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Marca un pendiente como pagado SIN crear transacción (bookkeeping). Se usa
/// cuando la transacción real ya se creó por otra vía (p. ej. el diálogo del
/// front), igual que SetBillingDate en recurrentes. Opcionalmente guarda el id
/// de esa transacción para trazabilidad.
/// </summary>
public class MarkPendingPaidCommand
{
    public int? TransactionId { get; set; }
}
