using Rithm.Platform.Security.Authorization;

namespace RithmTemplateApi.Middleware;

/// <summary>
/// Middleware that adds the X-Policy-Decision-Id header to HTTP responses.
/// Enables clients to track which policy decision authorized their request.
/// Links the response to the authorization decision for audit trail and debugging.
/// </summary>
public class PolicyDecisionHeaderMiddleware
{
    private readonly RequestDelegate _next;

    public PolicyDecisionHeaderMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context, IPolicyDecisionContext policyDecisionContext)
    {
        // Register callback to add header when response starts
        // Must be done before calling next() to ensure header is added before body is written
        context.Response.OnStarting(() =>
        {
            var policyDecisionId = policyDecisionContext.GetPolicyDecisionId();

            if (policyDecisionId.HasValue)
            {
                // Add X-Policy-Decision-Id header for traceability
                context.Response.Headers["X-Policy-Decision-Id"] = policyDecisionId.Value.ToString();
            }

            return Task.CompletedTask;
        });

        // Continue pipeline
        await _next(context);
    }
}
