using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Walletter.Application.Imports;

/// <summary>
/// Normalización de valores de un CSV de importación: fechas (varios formatos),
/// horas, tipos (income/expense) y montos con separadores es-VE o en-US.
/// </summary>
public static class ImportParsing
{
    private static readonly Regex ThousandsDot = new(@"^\d{1,3}(\.\d{3})+$", RegexOptions.Compiled);
    private static readonly Regex ThousandsComma = new(@"^\d{1,3}(,\d{3})+$", RegexOptions.Compiled);

    /// <summary>Acepta YYYY-MM-DD, DD/MM/YYYY, DD-MM-YYYY, YYYY/MM/DD. Sale YYYY-MM-DD.</summary>
    public static bool TryParseDate(string? raw, out string canonical)
    {
        canonical = string.Empty;
        var s = (raw ?? string.Empty).Trim();
        if (s.Length == 0) return false;

        // Ya viene en ISO.
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            canonical = iso.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }
        if (DateTime.TryParseExact(s, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso2))
        {
            canonical = iso2.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }
        // DD/MM/YYYY o DD-MM-YYYY (convención es-VE).
        foreach (var fmt in new[] { "d/M/yyyy", "d-M-yyyy", "dd/MM/yyyy", "dd-MM-yyyy" })
        {
            if (DateTime.TryParseExact(s, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                canonical = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            }
        }
        return false;
    }

    /// <summary>Acepta HH:MM o HH:MM:SS. Sale HH:MM. Vacío → false (el llamador decide el default).</summary>
    public static bool TryParseTime(string? raw, out string canonical)
    {
        canonical = string.Empty;
        var s = (raw ?? string.Empty).Trim();
        if (s.Length == 0) return false;
        foreach (var fmt in new[] { "h\\:mm", "hh\\:mm", "h\\:mm\\:ss", "hh\\:mm\\:ss" })
        {
            if (TimeSpan.TryParseExact(s, fmt, CultureInfo.InvariantCulture, out var t))
            {
                canonical = $"{t.Hours:D2}:{t.Minutes:D2}";
                return true;
            }
        }
        return false;
    }

    /// <summary>income: ingreso/income/i/credito/haber. expense: gasto/expense/g/debito/egreso/debe.</summary>
    public static bool TryParseType(string? raw, out string type)
    {
        type = string.Empty;
        var s = CsvReader.NormalizeHeader(raw ?? string.Empty);
        switch (s)
        {
            case "ingreso":
            case "income":
            case "i":
            case "credito":
            case "entrada":
            case "haber":
            case "abono":
                type = "income";
                return true;
            case "gasto":
            case "expense":
            case "g":
            case "debito":
            case "egreso":
            case "debe":
            case "cargo":
                type = "expense";
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Parsea un monto admitiendo formato es-VE (1.500,50) y en-US (1,500.50).
    /// Devuelve null si no hay dígitos.
    /// </summary>
    public static decimal? ParseMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var sb = new StringBuilder();
        foreach (var c in raw.Trim())
        {
            if (char.IsDigit(c) || c == ',' || c == '.' || c == '-' || c == '+') sb.Append(c);
        }
        var s = sb.ToString();
        if (s.Length == 0 || !s.Any(char.IsDigit)) return null;

        var hasComma = s.Contains(',');
        var hasDot = s.Contains('.');

        string normalized;
        if (hasComma && hasDot)
        {
            // El separador que aparece MÁS a la derecha es el decimal.
            var lastComma = s.LastIndexOf(',');
            var lastDot = s.LastIndexOf('.');
            if (lastComma > lastDot)
                normalized = s.Replace(".", "").Replace(',', '.');   // 1.500,50 → 1500.50
            else
                normalized = s.Replace(",", "");                      // 1,500.50 → 1500.50
        }
        else if (hasComma)
        {
            normalized = ThousandsComma.IsMatch(s) ? s.Replace(",", "") : s.Replace(',', '.');
        }
        else if (hasDot)
        {
            normalized = ThousandsDot.IsMatch(s) ? s.Replace(".", "") : s;
        }
        else
        {
            normalized = s;
        }

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
