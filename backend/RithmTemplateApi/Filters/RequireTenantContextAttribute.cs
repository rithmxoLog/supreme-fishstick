using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Rithm.Platform.Tenancy;

namespace RithmTemplateApi.Filters;

/// <summary>
/// Action filter que garantiza que el contexto de tenant esté inicializado.
/// Aplicar este atributo a controllers o actions que requieran tenant context obligatorio.
///
/// Este filtro es útil para endpoints S2S internos que SIEMPRE deben tener contexto de tenant,
/// sin excepciones. Complementa la validación de EcosystemIdentityMiddleware proporcionando
/// una validación explícita a nivel de controller/action.
/// </summary>
/// <example>
/// <code>
/// [RequireTenantContext]
/// [ApiController]
/// [Route("api/[controller]")]
/// public class InternalServiceController : ControllerBase
/// {
///     // Todos los endpoints de este controller requieren tenant context
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireTenantContextAttribute : ActionFilterAttribute
{
    /// <summary>
    /// Se ejecuta antes de la acción del controller.
    /// Valida que el tenant context esté inicializado, de lo contrario retorna 400 Bad Request.
    /// </summary>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var tenantContext = context.HttpContext.RequestServices
            .GetRequiredService<ITenantContext>();

        if (!tenantContext.IsInitialized)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Type = "https://rithmxo.com/problems/missing-tenant-context",
                Title = "Missing Tenant Context",
                Status = 400,
                Detail = "Service-to-service requests must include X-Tenant-Id header",
                Instance = context.HttpContext.Request.Path
            })
            {
                StatusCode = 400
            };
        }
    }
}
