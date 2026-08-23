using Walletter.Application.Auth;
using Microsoft.Extensions.Configuration;

namespace Walletter.Infrastructure.Auth;

/// <summary>
/// Lee las opciones de autenticación desde la configuración (appsettings/env).
/// Se registra en DI como IAuthOptions.
/// </summary>
public class AuthOptions : IAuthOptions
{
    public AuthOptions(IConfiguration config)
    {
        AuthUsername = config["AUTH_USERNAME"];
        AuthPassword = config["AUTH_PASSWORD"];
        JwtSecret = config["JWT_SECRET"];
    }

    public string? AuthUsername { get; }
    public string? AuthPassword { get; }
    public string? JwtSecret { get; }
}
