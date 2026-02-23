using Microsoft.Extensions.Diagnostics.HealthChecks;
using Rithm.Platform.ServiceDiscovery;
using System.Diagnostics;

namespace RithmTemplateApi.HealthChecks;

/// <summary>
/// Health check for InfraSoT connectivity.
/// Verifies that the service can reach the InfraSoT registry.
/// </summary>
public class InfraSoTHealthCheck : IHealthCheck
{
    private readonly IInfraSoTClient _infraSoTClient;
    private readonly ILogger<InfraSoTHealthCheck> _logger;

    public InfraSoTHealthCheck(
        IInfraSoTClient infraSoTClient,
        ILogger<InfraSoTHealthCheck> logger)
    {
        _infraSoTClient = infraSoTClient ?? throw new ArgumentNullException(nameof(infraSoTClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Check connectivity to InfraSoT
            var isConnected = await _infraSoTClient.CheckConnectivityAsync(cancellationToken);
            stopwatch.Stop();

            var responseTimeMs = stopwatch.ElapsedMilliseconds;

            if (isConnected)
            {
                return HealthCheckResult.Healthy(
                    $"InfraSoT is reachable (response time: {responseTimeMs}ms)",
                    new Dictionary<string, object>
                    {
                        ["responseTimeMs"] = responseTimeMs,
                        ["isConnected"] = true
                    });
            }
            else
            {
                return HealthCheckResult.Degraded(
                    $"InfraSoT is unreachable or unhealthy (response time: {responseTimeMs}ms)",
                    data: new Dictionary<string, object>
                    {
                        ["responseTimeMs"] = responseTimeMs,
                        ["isConnected"] = false
                    });
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "InfraSoT health check failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return HealthCheckResult.Unhealthy(
                $"InfraSoT health check failed: {ex.Message}",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
                    ["isConnected"] = false,
                    ["error"] = ex.Message
                });
        }
    }
}
