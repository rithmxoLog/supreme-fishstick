using Serilog.Core;
using Serilog.Events;
using Rithm.Platform.Security.Authorization;

namespace RithmTemplateApi.Observability;

/// <summary>
/// Serilog enricher that injects PolicyDecisionId into log events.
/// Links authorization decisions to logs for audit trail correlation.
/// </summary>
public class PolicyDecisionContextEnricher : ILogEventEnricher
{
    private readonly IPolicyDecisionContext? _policyDecisionContext;

    public PolicyDecisionContextEnricher(IPolicyDecisionContext? policyDecisionContext)
    {
        _policyDecisionContext = policyDecisionContext;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var policyDecisionId = _policyDecisionContext?.GetPolicyDecisionId();
        if (policyDecisionId.HasValue)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("PolicyDecisionId", policyDecisionId.Value));
        }
    }
}
