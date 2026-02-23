using Microsoft.AspNetCore.Mvc;
using Rithm.Platform.Tenancy;

namespace RithmTemplateApi.Controllers;

/// <summary>
/// Base controller for all RithmTemplate API controllers.
/// Provides common functionality and tenant context access.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public abstract class RithmTemplateBaseController : ControllerBase
{
    /// <summary>
    /// Gets the tenant context for the current request.
    /// Populated by EcosystemIdentityMiddleware from HTTP headers.
    /// </summary>
    protected ITenantContext TenantContext =>
        HttpContext.RequestServices.GetRequiredService<ITenantContext>();

    /// <summary>
    /// Gets the current tenant ID (primary multi-tenancy discriminator, Guid).
    /// </summary>
    protected Guid TenantId => TenantContext.TenantId;

    /// <summary>
    /// Gets the current actor ID (user or service performing the action).
    /// </summary>
    protected string? ActorId => TenantContext.ActorId;

    /// <summary>
    /// Gets the correlation ID for distributed tracing.
    /// </summary>
    protected string CorrelationId => TenantContext.CorrelationId;

    /// <summary>
    /// Creates a standardized OK response with data.
    /// </summary>
    protected IActionResult OkResponse<T>(T data, string? message = null)
    {
        return Ok(new
        {
            success = true,
            message = message ?? "Operation completed successfully",
            data
        });
    }

    /// <summary>
    /// Creates a standardized Created response for resource creation.
    /// </summary>
    protected IActionResult CreatedResponse<T>(string actionName, object routeValues, T data)
    {
        return CreatedAtAction(actionName, routeValues, new
        {
            success = true,
            message = "Resource created successfully",
            data
        });
    }

    /// <summary>
    /// Creates a 202 Accepted response for async/batch operations.
    /// Returns immediately with operation tracking information.
    /// </summary>
    protected IActionResult AcceptedResponse(string operationId, string statusEndpoint)
    {
        return Accepted(statusEndpoint, new
        {
            success = true,
            message = "Operation accepted and processing",
            operationId,
            statusUrl = statusEndpoint
        });
    }

    /// <summary>
    /// Creates a standardized NoContent response for updates.
    /// </summary>
    protected new IActionResult NoContent()
    {
        return base.NoContent();
    }

}
