namespace Walletter.Domain.Entities;

/// <summary>
/// Refresh token persistido para sesiones de login.
/// </summary>
public class RefreshToken
{
    public string Jti { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public long ExpiresAt { get; set; }
    public long CreatedAt { get; set; }
    public string? UserAgent { get; set; }
    public string? Ip { get; set; }
    public string? DeviceName { get; set; }
    public long? LastUsedAt { get; set; }
}
