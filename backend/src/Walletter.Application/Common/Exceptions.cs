namespace Walletter.Application.Common;

/// <summary>
/// Error de regla de negocio (validación semántica). El API lo mapea a un
/// 400/409 con mensaje legible.
/// </summary>
public class BusinessException : Exception
{
    public BusinessException(string message) : base(message) { }
}

/// <summary>
/// Recurso no encontrado → HTTP 404.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

/// <summary>
/// Sin autenticación/autorización → HTTP 401.
/// </summary>
public class UnauthorizedAppException : Exception
{
    public UnauthorizedAppException(string message) : base(message) { }
}
