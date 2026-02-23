using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RithmTemplateApi.Models;
using System.Diagnostics;
using System.Text.Json;

namespace RithmTemplateApi.HealthChecks;

/// <summary>
/// Custom health response writer that outputs Portcullis-compliant JSON.
/// </summary>
public static class PortcullisHealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    /// <summary>
    /// Writes the health check report in Portcullis JSON format.
    /// </summary>
    public static async Task WriteResponseAsync(
        HttpContext context,
        HealthReport report,
        string appId,
        string version,
        string environment)
    {
        var stopwatch = Stopwatch.StartNew();

        // Detectar si request es desde red de gestión
        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        var isManagementNetwork = ManagementNetworkDetector.IsManagementNetwork(remoteIp);
        var isProduction = environment.Equals("production", StringComparison.OrdinalIgnoreCase);
        var shouldAnonymize = isProduction && !isManagementNetwork;

        // Map overall health status
        var overallStatus = MapHealthStatus(report.Status);

        // Build component health statuses
        var components = new Dictionary<string, ComponentHealth>();

        foreach (var entry in report.Entries)
        {
            var componentStatus = MapHealthStatus(entry.Value.Status);

            if (shouldAnonymize)
            {
                // ANONIMIZAR en producción desde redes no-gestión
                components[entry.Key] = new ComponentHealth
                {
                    Status = componentStatus,
                    Description = null,
                    ResponseTimeMs = null,
                    Error = entry.Value.Exception != null ? "Error occurred" : null,
                    Data = null
                };
            }
            else
            {
                // DETALLES COMPLETOS para desarrollo o red de gestión
                var responseTimeMs = entry.Value.Data.TryGetValue("responseTimeMs", out var timeObj)
                    ? Convert.ToInt64(timeObj)
                    : (long?)null;

                var error = entry.Value.Exception?.Message
                            ?? (entry.Value.Data.TryGetValue("error", out var errorObj) ? errorObj?.ToString() : null);

                components[entry.Key] = new ComponentHealth
                {
                    Status = componentStatus,
                    Description = entry.Value.Description,
                    ResponseTimeMs = responseTimeMs,
                    Error = error,
                    Data = entry.Value.Data.Count > 0
                        ? entry.Value.Data.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value)
                        : null
                };
            }
        }

        stopwatch.Stop();

        // Build Portcullis response
        var response = new PortcullisHealthResponse
        {
            AppId = appId,
            Version = shouldAnonymize ? null : version,
            Environment = shouldAnonymize ? null : environment,
            Status = overallStatus,
            Timestamp = DateTime.UtcNow.ToString("o"),
            DurationMs = shouldAnonymize ? null : stopwatch.ElapsedMilliseconds,
            Components = components,
            Metadata = shouldAnonymize ? null : new Dictionary<string, object>
            {
                ["total_checks"] = report.Entries.Count,
                ["total_duration_ms"] = report.TotalDuration.TotalMilliseconds,
                ["source_network"] = remoteIp ?? "unknown"
            }
        };

        // Set HTTP status code based on health
        context.Response.StatusCode = report.Status switch
        {
            HealthStatus.Healthy => StatusCodes.Status200OK,
            HealthStatus.Degraded => StatusCodes.Status200OK, // Still OK but degraded
            HealthStatus.Unhealthy => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(response, JsonOptions));
    }

    /// <summary>
    /// Maps .NET HealthStatus to Portcullis health status string.
    /// </summary>
    private static string MapHealthStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => PortcullisHealthStatus.Healthy,
        HealthStatus.Degraded => PortcullisHealthStatus.Degraded,
        HealthStatus.Unhealthy => PortcullisHealthStatus.Unhealthy,
        _ => PortcullisHealthStatus.Unhealthy
    };
}

/// <summary>
/// Helper para detectar si una IP pertenece a la red de gestión interna.
/// Las redes de gestión tienen acceso a metadatos completos en health checks.
/// </summary>
public static class ManagementNetworkDetector
{
    private static readonly HashSet<string> ManagementPrefixes = new()
    {
        "10.",       // Private network 10.0.0.0/8
        "172.",      // Private network 172.16.0.0/12
        "192.168.",  // Private network 192.168.0.0/16
        "127."       // Localhost 127.0.0.0/8
    };

    /// <summary>
    /// Determina si una dirección IP pertenece a una red de gestión.
    /// </summary>
    /// <param name="ipAddress">La dirección IP a verificar</param>
    /// <returns>True si la IP es de una red de gestión, false en caso contrario</returns>
    public static bool IsManagementNetwork(string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return false;

        return ManagementPrefixes.Any(prefix => ipAddress.StartsWith(prefix));
    }
}
