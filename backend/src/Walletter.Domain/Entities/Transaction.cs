namespace Walletter.Domain.Entities;

/// <summary>
/// Transacción financiera. El monto se almacena en centavos (×100).
/// `ParentId` enlaza transacciones hijas (ej. comisiones/fees) con su padre.
/// Soft-delete vía `Deleted`.
/// </summary>
public class Transaction
{
    public int Id { get; set; }
    public int WalletId { get; set; }
    public int CategoryId { get; set; }
    public string Type { get; set; } = string.Empty; // income | expense
    public int Amount { get; set; } // centavos (×100)
    public string? Description { get; set; }

    /// <summary>Instante absoluto UTC.</summary>
    public DateTime DatetimeUtc { get; set; }

    /// <summary>Comisión denormalizada (centavos).</summary>
    public int Fee { get; set; }

    /// <summary>Id de la transacción padre (para fees/hijas).</summary>
    public int? ParentId { get; set; }

    public bool Deleted { get; set; }
    public DateTime CreatedAt { get; set; }

    public Wallet Wallet { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public Transaction? Parent { get; set; }
    public ICollection<Transaction> Children { get; set; } = new List<Transaction>();

    public ICollection<Exchange> DebitExchanges { get; set; } = new List<Exchange>();
    public ICollection<Exchange> CreditExchanges { get; set; } = new List<Exchange>();

    /// <summary>
    /// true si esta transacción es una COMISIÓN (fee): hija (ParentId != null)
    /// de categoría "fee". El monto de una comisión está contado dentro del fee
    /// denormalizado de su padre; identificarla evita revertirla dos veces.
    /// </summary>
    public bool IsFeeComision() =>
        ParentId != null && string.Equals(Category?.Name, "fee", StringComparison.OrdinalIgnoreCase);
}
