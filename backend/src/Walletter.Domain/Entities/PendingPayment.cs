namespace Walletter.Domain.Entities;

/// <summary>
/// Pago pendiente: deuda u obligación registrada que aún no se ha pagado.
/// No tiene periodicidad ni planificación: es un compromiso puntual que se
/// marca como pagado (se genera una transacción real) o se cancela.
/// </summary>
public class PendingPayment
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

    /// <summary>
    /// Fecha límite (due date) en la zona del usuario, formato yyyy-MM-dd.
    /// Null si no tiene vencimiento. Es informativa: el pago es puntual, no se
    /// planifica ni reprograma solo.
    /// </summary>
    public string? DueDate { get; set; }

    /// <summary>
    /// true si el pago ya se realizó (se generó una transacción real).
    /// </summary>
    public bool IsPaid { get; set; }

    /// <summary>
    /// Fecha en que se marcó como pagado (UTC). Null si aún está pendiente.
    /// </summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>
    /// Id de la transacción real generada al pagar. Null si no se ha pagado.
    /// </summary>
    public int? TransactionId { get; set; }

    /// <summary>
    /// true si se canceló el pendiente (soft delete). Un pendiente pagado o
    /// cancelado deja de aparecer en la lista activa.
    /// </summary>
    public bool IsCancelled { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
}
