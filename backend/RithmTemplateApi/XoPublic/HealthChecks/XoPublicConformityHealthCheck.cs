using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RithmTemplateApi.XoPublic.Services;

namespace RithmTemplateApi.XoPublic.HealthChecks;

/// <summary>
/// Health check that validates xo_public schema conformity (Pilar 5).
/// Checks: schema exists, all views have tenant_id, security user exists and has correct permissions.
/// Returns Degraded (not Unhealthy) on failure — a missing integrator user should not cause HTTP 503.
/// </summary>
public class XoPublicConformityHealthCheck : IHealthCheck
{
    private readonly IXoPublicSchemaDiscoveryService _discoveryService;
    private readonly ILogger<XoPublicConformityHealthCheck> _logger;

    public XoPublicConformityHealthCheck(
        IXoPublicSchemaDiscoveryService discoveryService,
        ILogger<XoPublicConformityHealthCheck> logger)
    {
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var validationResults = new Dictionary<string, object>();

        try
        {
            // Check 1: Schema exists
            var schemaExists = await _discoveryService.SchemaExistsAsync(cancellationToken);
            validationResults["schema_exists"] = schemaExists;

            if (!schemaExists)
            {
                stopwatch.Stop();
                validationResults["responseTimeMs"] = stopwatch.ElapsedMilliseconds;

                return HealthCheckResult.Degraded(
                    "xo_public schema does not exist",
                    data: validationResults);
            }

            // Check 2: All views have tenant_id
            var allViewsHaveTenantId = await _discoveryService.AllViewsHaveTenantIdAsync(cancellationToken);
            validationResults["all_views_have_tenant_id"] = allViewsHaveTenantId;

            // Check 3: Security user exists
            var userExists = await _discoveryService.SecurityUserExistsAsync(cancellationToken);
            validationResults["security_user_exists"] = userExists;

            // Check 4: Security user has correct permissions (only check if user exists)
            var correctPermissions = false;
            if (userExists)
            {
                correctPermissions = await _discoveryService.SecurityUserHasCorrectPermissionsAsync(cancellationToken);
            }
            validationResults["security_user_permissions_correct"] = correctPermissions;

            // Check 5: Get view count for metadata
            var views = await _discoveryService.GetViewsMetadataAsync(cancellationToken);
            validationResults["views_count"] = views.Count;

            stopwatch.Stop();
            validationResults["responseTimeMs"] = stopwatch.ElapsedMilliseconds;

            var allPassed = schemaExists && allViewsHaveTenantId && userExists && correctPermissions;

            if (allPassed)
            {
                return HealthCheckResult.Healthy(
                    $"xo_public conformity check passed ({stopwatch.ElapsedMilliseconds}ms, {views.Count} views)",
                    data: validationResults);
            }

            // Build description of what failed
            var failures = new List<string>();
            if (!allViewsHaveTenantId) failures.Add("views missing tenant_id");
            if (!userExists) failures.Add("security user missing");
            if (!correctPermissions) failures.Add("incorrect permissions");

            return HealthCheckResult.Degraded(
                $"xo_public conformity warnings: {string.Join(", ", failures)}",
                data: validationResults);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "xo_public conformity health check failed after {ElapsedMs}ms",
                stopwatch.ElapsedMilliseconds);

            return HealthCheckResult.Unhealthy(
                $"xo_public conformity check failed: {ex.Message}",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
                    ["error"] = ex.Message
                });
        }
    }
}
