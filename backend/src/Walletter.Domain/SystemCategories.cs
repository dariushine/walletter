namespace Walletter.Domain;

/// <summary>
/// Categorías de sistema que no se pueden editar/crear/eliminar por el usuario.
/// </summary>
public static class SystemCategories
{
    public const string Fee = "fee";
    public const string ExchangeOut = "exchange_out";
    public const string ExchangeIn = "exchange_in";

    public static readonly string[] All = { Fee, ExchangeOut, ExchangeIn };

    public static bool IsSystem(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Array.IndexOf(All, name.Trim()) >= 0;
    }
}
