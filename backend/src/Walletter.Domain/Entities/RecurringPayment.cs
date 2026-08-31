namespace Walletter.Domain.Entities;

/// <summary>
/// Pago recurrente. Al ejecutarlo se genera una transacción real.
/// </summary>
public class RecurringPayment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Amount { get; set; } // centavos
    public int Fee { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // income | expense
    public int CategoryId { get; set; }
    public int? WalletId { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// true si es una suscripción (pago fijo periódico con día de cobro).
    /// </summary>
    public bool IsSubscription { get; set; }

    /// <summary>
    /// Día del mes en que se cobra la suscripción (1-31).
    /// </summary>
    public int? BillingDay { get; set; }

    /// <summary>
    /// Última vez que se ejecutó el pago (se creó una transacción real).
    /// Null si nunca se ha ejecutado.
    /// </summary>
    public DateTime? LastExecutedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
}
