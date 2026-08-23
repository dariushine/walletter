using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Walletter.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Walletter.Api.Auth;

/// <summary>
/// Esquema de autenticación dual: acepta un access token JWT
/// (Authorization: Bearer o cookie access_token) O un API token
/// (X-Api-Key / Authorization: Bearer). Si ninguno es válido y la auth
/// está habilitada, el request queda sin autenticar (401 al exigir [Authorize]).
/// Cuando la auth está deshabilitada, se autentica automáticamente.
/// </summary>
public class ApiOrBearerHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IAuthOptions _options;
    private readonly AuthService _auth;

    public ApiOrBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IAuthOptions authOptions,
        AuthService auth)
        : base(options, logger, encoder)
    {
        _options = authOptions;
        _auth = auth;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Si la auth está deshabilitada, todo es anónimo-autenticado.
        if (!_auth.AuthEnabled)
        {
            var claims = new[] { new Claim("sub", "anonymous"), new Claim("anonymous", "true") };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        // 1) API token: header X-Api-Key o Authorization: Bearer (si no es JWT).
        var apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var bearer = authHeader["Bearer ".Length..].Trim();
            // Si parece JWT (2 puntos) lo tratamos como access token; si no, como API token.
            if (bearer.Count(c => c == '.') == 2)
            {
                return await ValidateAccessTokenAsync(bearer);
            }
            apiKey = bearer;
        }

        if (!string.IsNullOrEmpty(apiKey))
        {
            var ok = await _auth.ValidateApiToken(apiKey, Context.RequestAborted);
            if (ok)
            {
                var claims = new[] { new Claim("sub", "api"), new Claim("apikey", "true") };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
            }
            return AuthenticateResult.Fail("API token inválido");
        }

        // 2) Access token JWT: Authorization: Bearer JWT o cookie access_token.
        var accessToken = Request.Headers["Authorization"].FirstOrDefault() is string h && h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? h["Bearer ".Length..].Trim()
            : Request.Cookies["access_token"];

        if (string.IsNullOrEmpty(accessToken))
            return AuthenticateResult.NoResult();

        return await ValidateAccessTokenAsync(accessToken);
    }

    private async Task<AuthenticateResult> ValidateAccessTokenAsync(string token)
    {
        try
        {
            var handler = new JsonWebTokenHandler();
            var secret = _options.JwtSecret ?? "change-me-in-production";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromSeconds(30),
            };

            var result = await handler.ValidateTokenAsync(token, validationParameters);
            if (!result.IsValid || result.ClaimsIdentity is null)
                return AuthenticateResult.Fail("Access token inválido");

            var principal = new ClaimsPrincipal(result.ClaimsIdentity);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        catch
        {
            return AuthenticateResult.Fail("Access token inválido");
        }
    }
}
