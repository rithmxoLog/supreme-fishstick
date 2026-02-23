using Rithm.Platform.Tenancy;
using Rithm.Platform.Tenancy.Baggage;
using RithmTemplate.Application.Exceptions;

namespace RithmTemplateApi.Middleware;

/// <summary>
/// Middleware that extracts ecosystem identity from HTTP headers and populates ITenantContext.
/// This middleware enforces that ALL requests include valid tenant identification.
///
/// Expected Headers:
/// - X-Tenant-Id: GUID format tenant identifier (REQUIRED)
/// - X-App-Id: Application identifier (optional)
/// - X-Actor-Id: User/Service actor identifier (optional)
/// - X-Correlation-Id: Distributed tracing correlation ID (auto-generated if missing)
/// </summary>
public class EcosystemIdentityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<EcosystemIdentityMiddleware> _logger;

    private const string HeaderTenantId = "X-Tenant-Id";
    private const string HeaderAppId = "X-App-Id";
    private const string HeaderActorId = "X-Actor-Id";
    private const string HeaderCorrelationId = "X-Correlation-Id";

    public EcosystemIdentityMiddleware(
        RequestDelegate next,
        ILogger<EcosystemIdentityMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, IBaggageContext baggageContext)
    {
        // Skip for health check and metrics endpoints
        if (IsHealthOrMetricsEndpoint(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Extract and validate tenant headers
        var tenantIdHeader = context.Request.Headers[HeaderTenantId].FirstOrDefault();
        var appIdHeader = context.Request.Headers[HeaderAppId].FirstOrDefault();
        var actorIdHeader = context.Request.Headers[HeaderActorId].FirstOrDefault();
        var correlationIdHeader = context.Request.Headers[HeaderCorrelationId].FirstOrDefault();

        // Validate required headers
        if (string.IsNullOrWhiteSpace(tenantIdHeader))
        {
            _logger.LogWarning("Request rejected: Missing {Header} header", HeaderTenantId);
            throw new TenantContextMissingException(HeaderTenantId);
        }

        // Parse and validate TenantId
        if (!Guid.TryParse(tenantIdHeader, out var tenantId))
        {
            _logger.LogWarning("Request rejected: Invalid {Header} format: {Value}", HeaderTenantId, tenantIdHeader);
            throw new TenantContextMissingException(HeaderTenantId);
        }

        // Generate correlation ID if not provided
        var correlationId = string.IsNullOrWhiteSpace(correlationIdHeader)
            ? Guid.NewGuid().ToString("N")
            : correlationIdHeader;

        // Initialize tenant context (this is scoped per request)
        if (tenantContext is TenantContext mutableContext)
        {
            mutableContext.Initialize(tenantId, appIdHeader, actorIdHeader, correlationId);

            _logger.LogDebug(
                "Tenant context initialized: TenantId={TenantId}, ActorId={ActorId}, CorrelationId={CorrelationId}",
                tenantId, actorIdHeader, correlationId);
        }
        else
        {
            _logger.LogError("ITenantContext is not of type TenantContext. Cannot initialize.");
            throw new InvalidOperationException("Invalid ITenantContext implementation.");
        }

        // Add correlation ID to response headers for tracing
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderCorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        // Propagate ecosystem context to W3C Baggage for downstream services
        baggageContext.SetBaggage("tenant.id", tenantId.ToString());
        if (!string.IsNullOrEmpty(appIdHeader))
            baggageContext.SetBaggage("app.id", appIdHeader);
        if (!string.IsNullOrEmpty(actorIdHeader))
            baggageContext.SetBaggage("actor.id", actorIdHeader);
        baggageContext.SetBaggage("correlation.id", correlationId);

        _logger.LogDebug("W3C Baggage set for downstream propagation");

        await _next(context);
    }

    private static bool IsHealthOrMetricsEndpoint(PathString path)
    {
        return path.StartsWithSegments("/health") ||
               path.StartsWithSegments("/metrics") ||
               path.StartsWithSegments("/swagger");
    }
}
