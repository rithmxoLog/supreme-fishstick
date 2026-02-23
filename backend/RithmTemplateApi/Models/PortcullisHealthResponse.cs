using System.Text.Json.Serialization;

namespace RithmTemplateApi.Models;

/// <summary>
/// Portcullis-compliant health response schema.
/// Standardized format for health monitoring across the rithmXO ecosystem.
/// </summary>
public class PortcullisHealthResponse
{
    /// <summary>
    /// Application identifier.
    /// Example: "rithm-template-service"
    /// </summary>
    [JsonPropertyName("app_id")]
    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// Application version.
    /// Example: "1.0.0"
    /// Nullable para permitir anonimización en producción.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>
    /// Environment name.
    /// Example: "production", "staging", "development"
    /// Nullable para permitir anonimización en producción.
    /// </summary>
    [JsonPropertyName("environment")]
    public string? Environment { get; set; }

    /// <summary>
    /// Overall health status: Healthy, Degraded, Unhealthy.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when this health check was performed (ISO 8601 UTC).
    /// Example: "2026-02-06T12:34:56.789Z"
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>
    /// Total duration of the health check in milliseconds.
    /// Nullable para permitir anonimización en producción.
    /// </summary>
    [JsonPropertyName("duration_ms")]
    public long? DurationMs { get; set; }

    /// <summary>
    /// Individual component health statuses.
    /// </summary>
    [JsonPropertyName("components")]
    public Dictionary<string, ComponentHealth> Components { get; set; } = new();

    /// <summary>
    /// Additional metadata (optional).
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Health status of an individual component (database, cache, external service, etc.).
/// </summary>
public class ComponentHealth
{
    /// <summary>
    /// Component health status: Healthy, Degraded, Unhealthy.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of the component's health.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Response time in milliseconds (if applicable).
    /// </summary>
    [JsonPropertyName("response_time_ms")]
    public long? ResponseTimeMs { get; set; }

    /// <summary>
    /// Error message if the component is unhealthy.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Additional component-specific data.
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// Portcullis health status enum.
/// </summary>
public static class PortcullisHealthStatus
{
    public const string Healthy = "Healthy";
    public const string Degraded = "Degraded";
    public const string Unhealthy = "Unhealthy";
}
