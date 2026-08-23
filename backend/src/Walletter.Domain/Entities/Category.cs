namespace Walletter.Domain.Entities;

/// <summary>
/// Categoría de transacción (income | expense). Puede ser de sistema
/// (fee, exchange_out, exchange_in) y no se puede editar/eliminar.
/// </summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // income | expense
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<RecurringPayment> Recurring { get; set; } = new List<RecurringPayment>();
}
