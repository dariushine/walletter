namespace Walletter.Domain.Entities;

/// <summary>
/// Clave/valor para configuración (ej. user_timezone).
/// </summary>
public class Setting
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTime UpdatedAt { get; set; }
}
