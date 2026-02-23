using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Rithm.Platform.Observability;

namespace RithmTemplateApi.Observability;

/// <summary>
/// Extension methods for configuring OpenTelemetry instrumentation with OTLP export.
/// Registers tracing, metrics, and automatic instrumentation for ASP.NET Core, HttpClient, and EF Core.
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Adds OpenTelemetry instrumentation with distributed tracing and metrics.
    /// Exports telemetry to an OTLP collector (Jaeger, Tempo, etc.) via gRPC.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddOpenTelemetryInstrumentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otlpEnabled = configuration.GetValue<bool>("OpenTelemetry:Enabled", true);
        if (!otlpEnabled) return services;

        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? ObservabilityConstants.ServiceName;
        var serviceVersion = configuration["OpenTelemetry:ServiceVersion"] ?? ObservabilityConstants.ServiceVersion;
        var otlpEndpoint = configuration["OpenTelemetry:OtlpExporterEndpoint"] ?? "http://localhost:4317";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = configuration["InfraSoT:Environment"] ?? "development",
                    ["service.namespace"] = "rithm.ecosystem"
                }))
            .WithTracing(tracing => tracing
                // ASP.NET Core automatic instrumentation
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;

                    // Filter out health checks from traces
                    options.Filter = (ctx) => !ctx.Request.Path.StartsWithSegments("/health");

                    // Enrich traces with ecosystem joining keys from HTTP headers
                    options.EnrichWithHttpRequest = (activity, httpRequest) =>
                    {
                        if (httpRequest.Headers.TryGetValue("X-Correlation-Id", out var correlationId))
                            activity.SetTag("ecosystem.correlation_id", correlationId.ToString());

                        if (httpRequest.Headers.TryGetValue("X-Tenant-Id", out var tenantId))
                            activity.SetTag("tenant.id", tenantId.ToString());

                        if (httpRequest.Headers.TryGetValue("X-Actor-Id", out var actorId))
                            activity.SetTag("actor.id", actorId.ToString());

                        if (httpRequest.Headers.TryGetValue("X-App-Id", out var appId))
                            activity.SetTag("app.id", appId.ToString());
                    };
                })
                // HttpClient outbound request instrumentation
                .AddHttpClientInstrumentation()

                // Entity Framework Core database query instrumentation
                .AddEntityFrameworkCoreInstrumentation()

                // Custom activity sources for manual instrumentation
                .AddSource(ObservabilityConstants.MediatRActivitySource.Name)
                .AddSource(ObservabilityConstants.PolicyEngineActivitySource.Name)
                .AddSource(ObservabilityConstants.AuditAuthorityActivitySource.Name)
                .AddSource(ObservabilityConstants.MassiveOperationsActivitySource.Name)

                // OTLP Exporter for traces
                .AddOtlpExporter(opts =>
                {
                    opts.Endpoint = new Uri(otlpEndpoint);
                    opts.Protocol = OtlpExportProtocol.Grpc;
                }))
            .WithMetrics(metrics => metrics
                // ASP.NET Core automatic metrics (request duration, active requests, etc.)
                .AddAspNetCoreInstrumentation()

                // HttpClient automatic metrics (outbound request duration, failures, etc.)
                .AddHttpClientInstrumentation()

                // Custom meters for domain-specific metrics
                .AddMeter(ObservabilityConstants.PolicyEngineMeter.Name)
                .AddMeter(ObservabilityConstants.AuditAuthorityMeter.Name)
                .AddMeter(ObservabilityConstants.MassiveOperationsMeter.Name)

                // OTLP Exporter for metrics
                .AddOtlpExporter(opts =>
                {
                    opts.Endpoint = new Uri(otlpEndpoint);
                    opts.Protocol = OtlpExportProtocol.Grpc;
                }));

        return services;
    }
}
