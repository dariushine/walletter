namespace Walletter.Application.Transactions;

public class CreateTransactionCommand
{
    public int WalletId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // income | expense
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public decimal Fee { get; set; }
    public string Date { get; set; } = string.Empty; // YYYY-MM-DD
    public string Time { get; set; } = string.Empty; // HH:MM
    public string? Tz { get; set; }
}

public class UpdateTransactionCommand
{
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public string? CategoryName { get; set; }
    public string? Tz { get; set; }
}

public class AddFeeCommand
{
    public decimal Amount { get; set; }
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string? Tz { get; set; }
}

public class AssociateTransactionCommand
{
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Fee { get; set; }
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string? Tz { get; set; }
}

public class ListTransactionsQuery
{
    public int? Page { get; set; }
    public int? Limit { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public int? WalletId { get; set; }
}

/// <summary>Resultado de crear una transacción (y su posible fee inline).</summary>
public record TransactionCreatedResult(
    int Id,
    int? FeeTransactionId);
