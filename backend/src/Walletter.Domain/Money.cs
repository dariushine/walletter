namespace Walletter.Domain;

/// <summary>
/// Utilidades de dinero. Montos en enteros de centavos (×100), tasas en ×10000.
/// Según decisión de diseño: montos ×100, tasas ×10000 (evita errores de float).
/// </summary>
public static class Money
{
    /// <summary>unidades (ej. 1.50) → entero de centavos (150).</summary>
    public static int ToInt(decimal units)
    {
        return (int)Math.Round(units * 100m);
    }

    /// <summary>centavos (150) → unidades (1.50).</summary>
    public static decimal ToNum(int value)
    {
        return value / 100m;
    }

    /// <summary>unidades → entero de tasa ×10000.</summary>
    public static int ToRateInt(decimal units)
    {
        return (int)Math.Round(units * 10000m);
    }

    /// <summary>entero de tasa ×10000 → unidades.</summary>
    public static decimal ToRateNum(int value)
    {
        return value / 10000m;
    }
}
