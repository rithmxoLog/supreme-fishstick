using Serilog.Core;
using Serilog.Events;
using Rithm.Platform.Tenancy;

namespace RithmTemplateApi.Observability;

/// <summary>
/// Serilog enricher that injects ecosystem tenant context into all log events.
/// Automatically adds TenantId, ActorId, and CorrelationId as structured properties.
/// </summary>
public class TenantContextEnricher : ILogEventEnricher
{
    private readonly ITenantContext? _tenantContext;

    public TenantContextEnricher(ITenantContext? tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (_tenantContext == null || !_tenantContext.IsInitialized)
            return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TenantId", _tenantContext.TenantId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ActorId", _tenantContext.ActorId ?? "anonymous"));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", _tenantContext.CorrelationId ?? "N/A"));
    }
}
