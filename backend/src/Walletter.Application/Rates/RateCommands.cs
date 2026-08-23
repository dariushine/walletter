namespace Walletter.Application.Rates;

public class UpsertDailyRateCommand
{
    public string Date { get; set; } = string.Empty;
    public decimal Bcv { get; set; }
    public decimal Paralelo { get; set; }
    public string? Source { get; set; }
}

public class UpdateDailyRateCommand
{
    public decimal? Bcv { get; set; }
    public decimal? Paralelo { get; set; }
    public string? Source { get; set; }
}
