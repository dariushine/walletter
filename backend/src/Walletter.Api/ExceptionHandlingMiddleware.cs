using System.Text.Json;
using Walletter.Application.Common;

namespace Walletter.Api;

/// <summary>
/// Convierte excepciones de aplicación en respuestas HTTP con JSON legible.
/// - BusinessException → 400 (regla de negocio)
/// - NotFoundException → 404
/// - UnauthorizedAppException → 401
/// - cualquier otra → 500
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var (status, message) = ex switch
            {
                BusinessException b => (400, b.Message),
                NotFoundException n => (404, n.Message),
                UnauthorizedAppException u => (401, u.Message),
                _ => (500, "Error interno del servidor"),
            };

            if (status == 500)
                _logger.LogError(ex, "Unhandled exception");

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }));
            }
        }
    }
}
