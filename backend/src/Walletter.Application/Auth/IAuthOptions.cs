namespace Walletter.Application.Auth;

/// <summary>
/// Opciones de autenticación expuestas a la capa de Aplicación (provider-agnóstico).
/// La implementación lee del configuration (env) en Infrastructure/Api.
/// </summary>
public interface IAuthOptions
{
    string? AuthUsername { get; }
    string? AuthPassword { get; }
    string? JwtSecret { get; }
}
