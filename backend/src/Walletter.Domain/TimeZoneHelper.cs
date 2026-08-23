namespace Walletter.Domain;

/// <summary>
/// Conversión de zonas horarias sin dependencias externas.
/// Modelo: un solo instante UTC (`datetime_utc`). El backend guarda instantes
/// UTC; el front proyecta a la zona del usuario.
/// Usa TimeZoneInfo de .NET (base datos tz = IANA instalada por defecto en Linux).
/// </summary>
public static class TimeZoneHelper
{
    public const string DefaultTimeZone = "America/Caracas";

    public static bool IsValidTimeZone(string? tz)
    {
        if (string.IsNullOrWhiteSpace(tz)) return false;
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(tz);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fecha/hora "de pared" del usuario (YYYY-MM-DD, HH:MM) en su zona → instante UTC.
    /// </summary>
    public static DateTime ToUtcInstant(string date, string time, string timeZone)
    {
        // Interpreta la hora de pared como "sin zona" (kind Unspecified) y luego la
        // trata como hora local de timeZone para obtener el instante absoluto.
        var naive = DateTime.ParseExact($"{date}T{time}:00", "yyyy-MM-dd'T'HH:mm:ss", null);
        var tzi = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(naive, DateTimeKind.Unspecified), tzi);
    }

    /// <summary>
    /// Instante UTC → {date (YYYY-MM-DD), time (HH:MM)} en la zona del usuario.
    /// </summary>
    public static (string Date, string Time) UtcToWallClock(DateTime dt, string timeZone)
    {
        var tzi = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dt, DateTimeKind.Utc), tzi);
        return (local.ToString("yyyy-MM-dd"), local.ToString("HH:mm"));
    }
}
