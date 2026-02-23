using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Rithm.Platform.ServiceDiscovery;
using Rithm.Platform.ServiceDiscovery.Models;
using RithmTemplateApi.XoPublic.Services;

namespace RithmTemplateApi.Services;

/// <summary>
/// Background service that handles self-registration with InfraSoT on startup,
/// sends periodic health status updates (heartbeat), and deregisters on shutdown.
/// Includes xo_public capability discovery for IntegratorXO autodiscovery (Pilar 3).
/// </summary>
public class InfraSoTRegistrationService : BackgroundService
{
    private readonly IInfraSoTClient _infraSoTClient;
    private readonly HealthCheckService _healthCheckService;
    private readonly InfraSoTBootstrapConfig _config;
    private readonly ILogger<InfraSoTRegistrationService> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IServiceProvider _serviceProvider;

    private string? _serviceInstanceId;
    private int _heartbeatIntervalSeconds = 30;

    public InfraSoTRegistrationService(
        IInfraSoTClient infraSoTClient,
        HealthCheckService healthCheckService,
        IOptions<InfraSoTBootstrapConfig> config,
        ILogger<InfraSoTRegistrationService> logger,
        IHostApplicationLifetime applicationLifetime,
        IServiceProvider serviceProvider)
    {
        _infraSoTClient = infraSoTClient ?? throw new ArgumentNullException(nameof(infraSoTClient));
        _healthCheckService = healthCheckService ?? throw new ArgumentNullException(nameof(healthCheckService));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InfraSoTRegistrationService starting...");

        try
        {
            // Wait for application to fully start before registering
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            // Register with InfraSoT
            await RegisterWithInfraSoTAsync(stoppingToken);

            // Start heartbeat loop
            await HeartbeatLoopAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("InfraSoTRegistrationService is stopping due to cancellation.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InfraSoTRegistrationService encountered an unexpected error.");

            // If FailFastOnStartup is true and we couldn't register, stop the application
            if (_config.FailFastOnStartup && _serviceInstanceId == null)
            {
                _logger.LogCritical(
                    "Failed to register with InfraSoT and FailFastOnStartup is enabled. Stopping application.");
                _applicationLifetime.StopApplication();
            }
        }
        finally
        {
            // Deregister on shutdown
            await DeregisterFromInfraSoTAsync();
        }
    }

    /// <summary>
    /// Registers this service with InfraSoT.
    /// </summary>
    private async Task RegisterWithInfraSoTAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Registering with InfraSoT. AppId: {AppId}, Environment: {Environment}, Version: {Version}",
                _config.AppId,
                _config.Environment,
                _config.Version);

            // Determine hostname and port (from environment or default)
            var hostname = Environment.GetEnvironmentVariable("SERVICE_HOSTNAME")
                           ?? Environment.GetEnvironmentVariable("HOSTNAME")
                           ?? "localhost";

            var portString = Environment.GetEnvironmentVariable("SERVICE_PORT")
                             ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Split(';').FirstOrDefault()?.Split(':').LastOrDefault()
                             ?? "5000";

            if (!int.TryParse(portString, out var port))
                port = 5000;

            var request = new ServiceRegistrationRequest
            {
                AppId = _config.AppId,
                Version = _config.Version,
                HostName = hostname,
                Port = port,
                HealthCheckUrl = "/health/ready",
                Environment = _config.Environment,
                Metadata = new Dictionary<string, string>
                {
                    ["framework"] = "ASP.NET Core 8.0",
                    ["registered_at"] = DateTime.UtcNow.ToString("o")
                }
            };

            // Discover xo_public capabilities for IntegratorXO autodiscovery (Pilar 3)
            await DiscoverXoPublicCapabilitiesAsync(request, cancellationToken);

            var response = await _infraSoTClient.RegisterServiceAsync(request, cancellationToken);

            _serviceInstanceId = response.ServiceInstanceId;
            _heartbeatIntervalSeconds = response.HeartbeatIntervalSeconds;

            _logger.LogInformation(
                "Successfully registered with InfraSoT. InstanceId: {InstanceId}, Heartbeat interval: {IntervalSeconds}s",
                _serviceInstanceId,
                _heartbeatIntervalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register with InfraSoT.");

            if (_config.FailFastOnStartup)
            {
                throw; // Propagate exception to stop the service
            }

            // If not fail-fast, log and continue (will retry in heartbeat loop)
            _logger.LogWarning("Continuing without InfraSoT registration (FailFastOnStartup is disabled).");
        }
    }

    /// <summary>
    /// Periodic heartbeat loop that sends health status updates to InfraSoT.
    /// </summary>
    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting heartbeat loop. Interval: {IntervalSeconds}s", _heartbeatIntervalSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_heartbeatIntervalSeconds), cancellationToken);

                // If not registered yet, try to register again
                if (string.IsNullOrEmpty(_serviceInstanceId))
                {
                    _logger.LogWarning("Service not registered with InfraSoT. Attempting registration...");
                    await RegisterWithInfraSoTAsync(cancellationToken);
                    continue; // Skip health update if registration just happened
                }

                // Get current health status
                var healthReport = await _healthCheckService.CheckHealthAsync(cancellationToken);
                var healthStatus = MapHealthStatus(healthReport.Status);

                // Build health data
                var healthData = new Dictionary<string, object>
                {
                    ["total_duration_ms"] = healthReport.TotalDuration.TotalMilliseconds,
                    ["total_checks"] = healthReport.Entries.Count,
                    ["timestamp"] = DateTime.UtcNow.ToString("o")
                };

                // Add component statuses
                foreach (var entry in healthReport.Entries)
                {
                    healthData[$"component_{entry.Key}_status"] = entry.Value.Status.ToString();
                    if (entry.Value.Data.TryGetValue("responseTimeMs", out var responseTime))
                        healthData[$"component_{entry.Key}_response_ms"] = responseTime;
                }

                // Send health update to InfraSoT
                var updateRequest = new HealthStatusUpdateRequest
                {
                    ServiceInstanceId = _serviceInstanceId,
                    Status = healthStatus,
                    HealthData = healthData
                };

                await _infraSoTClient.UpdateHealthStatusAsync(updateRequest, cancellationToken);

                _logger.LogDebug(
                    "Sent health status update to InfraSoT. Status: {Status}, InstanceId: {InstanceId}",
                    healthStatus,
                    _serviceInstanceId);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send heartbeat to InfraSoT. Will retry in {IntervalSeconds}s.",
                    _heartbeatIntervalSeconds);
                // Continue loop - don't crash the background service
            }
        }

        _logger.LogInformation("Heartbeat loop stopped.");
    }

    /// <summary>
    /// Deregisters this service from InfraSoT on shutdown.
    /// </summary>
    private async Task DeregisterFromInfraSoTAsync()
    {
        if (string.IsNullOrEmpty(_serviceInstanceId))
        {
            _logger.LogDebug("Service was not registered with InfraSoT. Skipping deregistration.");
            return;
        }

        try
        {
            _logger.LogInformation("Deregistering from InfraSoT. InstanceId: {InstanceId}", _serviceInstanceId);

            await _infraSoTClient.DeregisterServiceAsync(_serviceInstanceId, CancellationToken.None);

            _logger.LogInformation("Successfully deregistered from InfraSoT.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deregister from InfraSoT. InstanceId: {InstanceId}", _serviceInstanceId);
            // Don't throw - we're shutting down anyway
        }
    }

    /// <summary>
    /// Discovers xo_public views and adds capability metadata to the registration request.
    /// Uses IServiceProvider to resolve the discovery service (nullable — module may be disabled).
    /// Wrapped in try/catch to ensure registration proceeds even if discovery fails.
    /// </summary>
    private async Task DiscoverXoPublicCapabilitiesAsync(
        ServiceRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Resolve via scope because IXoPublicSchemaDiscoveryService is scoped (depends on scoped DbContext)
            using var scope = _serviceProvider.CreateScope();
            var discoveryService = scope.ServiceProvider.GetService<IXoPublicSchemaDiscoveryService>();

            if (discoveryService == null)
            {
                _logger.LogDebug("XoPublic module not registered — skipping capability discovery.");
                return;
            }

            var schemaExists = await discoveryService.SchemaExistsAsync(cancellationToken);
            if (!schemaExists)
            {
                _logger.LogDebug("xo_public schema not found — skipping capability registration.");
                return;
            }

            var views = await discoveryService.GetViewsMetadataAsync(cancellationToken);
            if (views.Count == 0)
            {
                _logger.LogDebug("No views found in xo_public — skipping capability registration.");
                return;
            }

            request.Capabilities ??= new List<ServiceCapability>();
            request.Capabilities.Add(new ServiceCapability
            {
                Type = "xo_public",
                Version = "1.0",
                Metadata = new Dictionary<string, object>
                {
                    ["view_count"] = views.Count,
                    ["view_names"] = views.Select(v => v.ViewName).ToList(),
                    ["metadata_endpoint"] = "/api/xo/metadata"
                }
            });

            _logger.LogInformation(
                "Registered xo_public capability with InfraSoT: {ViewCount} views ({ViewNames})",
                views.Count,
                string.Join(", ", views.Select(v => v.ViewName)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to discover xo_public views for InfraSoT registration. Continuing without capabilities.");
        }
    }

    /// <summary>
    /// Maps .NET HealthStatus to InfraSoT health status string.
    /// </summary>
    private static string MapHealthStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "Healthy",
        HealthStatus.Degraded => "Degraded",
        HealthStatus.Unhealthy => "Unhealthy",
        _ => "Unhealthy"
    };
}
