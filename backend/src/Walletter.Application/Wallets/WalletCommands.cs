namespace Walletter.Application.Wallets;

public class CreateWalletCommand
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal? Balance { get; set; }
    public string? Alias { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
}

public class UpdateWalletCommand
{
    public string? Name { get; set; }
    public decimal? Balance { get; set; }
    public string? Alias { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public string? Type { get; set; }
    public string? Currency { get; set; }
}
