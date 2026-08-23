namespace Walletter.Domain.Entities;

/// <summary>
/// Tasa de cambio diaria (BCV / paralelo), VES por USD. Tasas en ×10000.
/// </summary>
public class DailyRate
{
    public int Id { get; set; }
    public string Date { get; set; } = string.Empty; // YYYY-MM-DD
    public int Bcv { get; set; }       // ×10000
    public int Paralelo { get; set; }  // ×10000
    public string Source { get; set; } = "dolarapi";
    public DateTime CreatedAt { get; set; }
}
