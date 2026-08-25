namespace Walletter.Domain.Entities;

/// <summary>
/// Cambio de divisa entre dos billeteras. El débito y crédito son
/// transacciones que se generan atómicamente. Tasa ×10000.
/// </summary>
public class Exchange
{
    public int Id { get; set; }
    public int DebitTransactionId { get; set; }
    public int CreditTransactionId { get; set; }
    public int FromWalletId { get; set; }
    public int ToWalletId { get; set; }
    public int FromAmount { get; set; } // centavos
    public int ToAmount { get; set; }   // centavos
    public int Rate { get; set; }       // ×10000
    public int Fee { get; set; }        // centavos (débito)
    public int CreditFee { get; set; }  // centavos (crédito)
    public string? Description { get; set; }
    public bool Deleted { get; set; }

    /// <summary>
    /// Fecha y hora efectiva del cambio (UTC), distinta del CreatedAt.
    /// </summary>
    public DateTime DatetimeUtc { get; set; }

    public DateTime CreatedAt { get; set; }

    public Transaction Debit { get; set; } = null!;
    public Transaction Credit { get; set; } = null!;
    public Wallet From { get; set; } = null!;
    public Wallet To { get; set; } = null!;
}
