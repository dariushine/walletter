namespace Walletter.Application.Auth;

/// <summary>
/// Firma validación de access tokens (JWT) y hashing. La implementación
/// concreta vive en Infrastructure/Api.
/// </summary>
public interface ITokenService
{
    /// <summary>Firma un access token JWT para el subject dado.</summary>
    Task<string> SignAccessTokenAsync(string sub);

    /// <summary>Hash SHA-256 hex del valor.</summary>
    string Sha256(string value);

    /// <summary>Genera un secret aleatorio (para refresh tokens).</summary>
    string RandomSecret();

    /// <summary>Genera un UUID (jti).</summary>
    string NewJti();
}
