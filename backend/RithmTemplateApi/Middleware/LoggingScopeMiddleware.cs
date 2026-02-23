using Serilog.Context;
using Rithm.Platform.Tenancy;
using Rithm.Platform.Security.Authorization;

namespace RithmTemplateApi.Middleware;

/// <summary>
/// Middleware that pushes ecosystem context (TenantId, CorrelationId, etc.)
/// into Serilog's LogContext at the start of each request.
/// All subsequent logs within the request scope will automatically include these properties.
/// </summary>
public class LoggingScopeMiddleware
{
    private readonly RequestDelegate _next;

    public LoggingScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        IPolicyDecisionContext policyDecisionContext)
    {
        // Skip for health/metrics endpoints to avoid noise
        if (context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/metrics"))
        {
            await _next(context);
            return;
        }

        // Obtener Activity actual (OpenTelemetry trace)
        var activity = System.Diagnostics.Activity.Current;
        var traceId = activity?.TraceId.ToString() ?? "no-trace";
        var spanId = activity?.SpanId.ToString() ?? "no-span";

        // Push ecosystem context + TraceId into Serilog LogContext
        // These properties will be automatically included in all logs within this scope
        using (LogContext.PushProperty("TenantId", tenantContext.TenantId))
        using (LogContext.PushProperty("CorrelationId", tenantContext.CorrelationId))
        using (LogContext.PushProperty("ActorId", tenantContext.ActorId ?? "anonymous"))
        using (LogContext.PushProperty("AppId", tenantContext.AppId ?? "unknown"))
        using (LogContext.PushProperty("TraceId", traceId))
        using (LogContext.PushProperty("SpanId", spanId))
        {
            await _next(context);
        }
    }
}
