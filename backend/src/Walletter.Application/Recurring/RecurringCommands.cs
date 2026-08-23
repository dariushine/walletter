namespace Walletter.Application.Recurring;

public class CreateRecurringCommand
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
}

public class UpdateRecurringCommand
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Fee { get; set; }
    public string? Currency { get; set; }
    public string? Type { get; set; }
    public string? CategoryName { get; set; }
    public int? WalletId { get; set; }
}

public class ExecuteRecurringCommand
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
