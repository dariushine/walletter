namespace Walletter.Domain.Entities;

/// <summary>
/// Token de API para integraciones (el API key que usa el plugin/agente).
/// </summary>
public class ApiToken
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public long? ExpiresAt { get; set; }
    public long CreatedAt { get; set; }
    public long? LastUsedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
