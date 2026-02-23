using Microsoft.AspNetCore.Mvc;

namespace RithmTemplateApi.Middleware;

/// <summary>
/// Middleware que restringe acceso a Swagger UI a:
/// 1. Ambiente de desarrollo (sin restricciones)
/// 2. Producción con autenticación + claim 'swagger:read'
///
/// Este middleware protege la documentación de API de acceso no autorizado en producción,
/// previniendo la divulgación de información sobre la superficie de API, modelos de datos,
/// y endpoints disponibles.
/// </summary>
public class SwaggerAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SwaggerAuthorizationMiddleware> _logger;
    private readonly bool _isDevelopment;

    public SwaggerAuthorizationMiddleware(
        RequestDelegate next,
        IWebHostEnvironment environment,
        ILogger<SwaggerAuthorizationMiddleware> logger)
    {
        _next = next;
        _isDevelopment = environment.IsDevelopment();
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Solo verificar rutas de Swagger
        if (!path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Permitir en desarrollo sin restricciones
        if (_isDevelopment)
        {
            await _next(context);
            return;
        }

        // PRODUCCIÓN: Requerir autenticación
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            _logger.LogWarning(
                "Unauthorized Swagger access attempt from {IP}",
                context.Connection.RemoteIpAddress);

            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Type = "https://rithmxo.com/problems/unauthorized",
                Title = "Unauthorized",
                Status = 401,
                Detail = "Swagger UI requires authentication in production"
            });
            return;
        }

        // PRODUCCIÓN: Requerir claim 'swagger:read'
        if (!context.User.HasClaim("permission", "swagger:read"))
        {
            _logger.LogWarning(
                "Forbidden Swagger access attempt by user {User} from {IP}",
                context.User.Identity?.Name,
                context.Connection.RemoteIpAddress);

            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Type = "https://rithmxo.com/problems/forbidden",
                Title = "Forbidden",
                Status = 403,
                Detail = "Swagger UI requires 'swagger:read' permission"
            });
            return;
        }

        await _next(context);
    }
}
