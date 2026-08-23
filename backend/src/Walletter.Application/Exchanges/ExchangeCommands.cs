namespace Walletter.Application.Exchanges;

public class CreateExchangeCommand
{
    public int FromWalletId { get; set; }
    public int ToWalletId { get; set; }
    public decimal FromAmount { get; set; }
    public decimal ToAmount { get; set; }
    public string? Description { get; set; }
    public decimal Fee { get; set; }
    public decimal CreditFee { get; set; }
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string? Tz { get; set; }
}

public class UpdateExchangeCommand
{
    public decimal? FromAmount { get; set; }
    public decimal? ToAmount { get; set; }
    public decimal? Fee { get; set; }
    public decimal? CreditFee { get; set; }
    public string? Description { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public string? Tz { get; set; }
}

public class ListExchangesQuery
{
    public int? Page { get; set; }
    public int? Limit { get; set; }
    public string? Period { get; set; }
}
