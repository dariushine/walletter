using System.Security.Cryptography;
using System.Text;
using Walletter.Application.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Walletter.Infrastructure.Auth;

/// <summary>
/// Implementación de firma JWT (access token), hashing SHA-256 y generación
/// de secrets. Usa Microsoft.IdentityModel (sin dependencias de framework pesadas).
/// </summary>
public class TokenService : ITokenService
{
    private readonly IAuthOptions _options;

    public TokenService(IAuthOptions options)
    {
        _options = options;
    }

    public Task<string> SignAccessTokenAsync(string sub)
    {
        var handler = new JsonWebTokenHandler();
        var secret = _options.JwtSecret ?? "change-me-in-production";
        if (secret == "change-me-in-production")
            throw new InvalidOperationException("JWT_SECRET debe configurarse en producción (AUTH habilitada). No usar el valor por defecto.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("sub", sub),
            }),
            Expires = now.AddMinutes(15),
            IssuedAt = now,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };
        return Task.FromResult(handler.CreateToken(descriptor));
    }

    public string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public string RandomSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public string NewJti() => Guid.NewGuid().ToString("N");
}
