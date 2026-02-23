using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rithm.Platform.Security.Certificates;
using System.Security.Cryptography.X509Certificates;

namespace RithmTemplateApi.Middleware;

/// <summary>
/// Middleware that validates mTLS client certificates for incoming requests from Gateway #2.
/// Ensures that only requests with valid, trusted certificates are processed.
/// </summary>
public class MutualTlsValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ICertificateProvider _certificateProvider;
    private readonly ILogger<MutualTlsValidationMiddleware> _logger;
    private readonly MutualTlsConfiguration _config;

    public MutualTlsValidationMiddleware(
        RequestDelegate next,
        ICertificateProvider certificateProvider,
        IOptions<MutualTlsConfiguration> config,
        ILogger<MutualTlsValidationMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _certificateProvider = certificateProvider ?? throw new ArgumentNullException(nameof(certificateProvider));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip validation if mTLS is disabled (development mode)
        if (!_config.Enabled)
        {
            _logger.LogDebug("mTLS validation is disabled. Skipping certificate validation.");
            await _next(context);
            return;
        }

        // Skip validation for health/metrics endpoints
        if (IsHealthOrMetricsEndpoint(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Get the client certificate from the connection
        var clientCertificate = await context.Connection.GetClientCertificateAsync();

        // If no certificate provided and mTLS is required, reject the request
        if (clientCertificate == null && _config.RequireClientCertificate)
        {
            _logger.LogWarning(
                "mTLS validation failed: No client certificate provided. Path: {Path}, RemoteIP: {RemoteIP}",
                context.Request.Path,
                context.Connection.RemoteIpAddress);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                title = "Unauthorized",
                status = 401,
                detail = "Client certificate required for mTLS authentication.",
                instance = context.Request.Path.Value
            });
            return;
        }

        // If certificate provided, validate it
        if (clientCertificate != null)
        {
            var isValid = _certificateProvider.ValidateClientCertificate(
                clientCertificate,
                chain: null,
                sslPolicyErrors: System.Net.Security.SslPolicyErrors.None);

            if (!isValid)
            {
                _logger.LogWarning(
                    "mTLS validation failed: Invalid client certificate. Subject: {Subject}, Issuer: {Issuer}, Thumbprint: {Thumbprint}, Path: {Path}",
                    clientCertificate.Subject,
                    clientCertificate.Issuer,
                    clientCertificate.Thumbprint,
                    context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                    title = "Forbidden",
                    status = 403,
                    detail = "Client certificate validation failed. Certificate is not trusted or is invalid.",
                    instance = context.Request.Path.Value
                });
                return;
            }

            _logger.LogDebug(
                "mTLS validation successful. Subject: {Subject}, Thumbprint: {Thumbprint}",
                clientCertificate.Subject,
                clientCertificate.Thumbprint);

            // Store certificate info in HttpContext for downstream use
            context.Items["ClientCertificate"] = clientCertificate;
            context.Items["ClientCertificateThumbprint"] = clientCertificate.Thumbprint;
            context.Items["ClientCertificateSubject"] = clientCertificate.Subject;
        }

        // Continue to next middleware
        await _next(context);
    }

    /// <summary>
    /// Checks if the request path is a health or metrics endpoint that should skip mTLS validation.
    /// </summary>
    private static bool IsHealthOrMetricsEndpoint(PathString path) =>
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/metrics") ||
        path.StartsWithSegments("/swagger");
}

/// <summary>
/// Configuration for mTLS validation middleware.
/// </summary>
public class MutualTlsConfiguration
{
    /// <summary>
    /// Whether mTLS validation is enabled.
    /// Should be true in production, can be false in development.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Whether to require a client certificate for all requests.
    /// If false, requests without certificates are allowed (but certificates will be validated if present).
    /// </summary>
    public bool RequireClientCertificate { get; set; } = true;
}
