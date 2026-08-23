namespace Walletter.Domain.Entities;

/// <summary>
/// Billetera (monedero). Los montos de balance se almacenan en enteros de
/// centavos (×100) para evitar errores de punto flotante.
/// </summary>
public class Wallet
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;

    /// <summary>Balance en centavos (×100).</summary>
    public int Balance { get; set; }

    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ExcludeFromTotal { get; set; }
    public bool HideInDashboard { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<Exchange> ExchangesFrom { get; set; } = new List<Exchange>();
    public ICollection<Exchange> ExchangesTo { get; set; } = new List<Exchange>();
}
