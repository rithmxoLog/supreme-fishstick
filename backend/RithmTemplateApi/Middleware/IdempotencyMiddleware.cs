using Microsoft.Extensions.Options;
using Rithm.Platform.Tenancy;
using Rithm.Infrastructure.Valkey;
using RithmTemplateApi.Metrics;
using RithmTemplateApi.Middleware.Configuration;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RithmTemplateApi.Middleware;

/// <summary>
/// Middleware for handling idempotent requests using Idempotency-Key header.
/// Stores request results in Valkey/Redis to prevent duplicate operations.
/// SECURITY: All cached data is scoped to TenantId for multi-tenant isolation.
/// </summary>
public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;
    private readonly IValkeyClient _valkeyClient;
    private readonly IdempotencyMetrics? _metrics;
    private readonly IdempotencyConfiguration _config;
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    // HTTP methods that support idempotency
    private static readonly HashSet<string> IdempotentMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH"
    };

    public IdempotencyMiddleware(
        RequestDelegate next,
        ILogger<IdempotencyMiddleware> logger,
        IValkeyClient valkeyClient,
        IOptions<IdempotencyConfiguration> config,
        IdempotencyMetrics? metrics = null)
    {
        _next = next;
        _logger = logger;
        _valkeyClient = valkeyClient;
        _metrics = metrics;
        _config = config.Value;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        // Only process idempotent methods
        if (!IdempotentMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Check for Idempotency-Key header
        if (!context.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var idempotencyKey) ||
            string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // No idempotency key provided - proceed normally
            await _next(context);
            return;
        }

        // BUG FIX 3: Validate idempotency key length
        if (idempotencyKey.ToString().Length > _config.MaxKeyLength)
        {
            _logger.LogWarning("Idempotency-Key exceeds maximum length of {MaxLength} characters", _config.MaxKeyLength);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Invalid idempotency key",
                message = $"Idempotency-Key header must not exceed {_config.MaxKeyLength} characters"
            }));
            return;
        }

        // BUG FIX 2: Include TenantId in cache key for multi-tenant isolation
        var tenantId = tenantContext.TenantId.ToString();
        if (tenantContext.TenantId == Guid.Empty)
        {
            _logger.LogWarning("Missing tenant context for idempotency key: {Key}", idempotencyKey.ToString());
            await _next(context);
            return;
        }

        var cacheKey = ValkeyKeyBuilder.BuildIdempotencyKey(tenantId, idempotencyKey.ToString());

        try
        {
            // Try to get cached response
            var cachedResponse = await _valkeyClient.GetAsync<IdempotencyRecord>(cacheKey);

            if (cachedResponse != null)
            {
                _metrics?.RecordCacheHit(tenantId, context.Request.Method);
                _logger.LogInformation(
                    "Returning cached response for idempotency key: {IdempotencyKey}",
                    idempotencyKey.ToString());

                // Return cached response
                context.Response.StatusCode = cachedResponse.StatusCode;
                context.Response.ContentType = cachedResponse.ContentType;

                foreach (var header in cachedResponse.Headers)
                {
                    context.Response.Headers[header.Key] = header.Value;
                }

                // RFC 9110: Signal that this is a replayed response
                context.Response.Headers["Idempotency-Replay"] = "true";

                if (!string.IsNullOrEmpty(cachedResponse.Body))
                {
                    await context.Response.WriteAsync(cachedResponse.Body);
                }

                return;
            }

            // Try to acquire lock (prevent race conditions)
            var lockKey = $"{cacheKey}:lock";
            var lockStopwatch = Stopwatch.StartNew();
            var lockAcquired = await _valkeyClient.SetIfNotExistsAsync(lockKey, "processing", TimeSpan.FromSeconds(30));
            lockStopwatch.Stop();
            _metrics?.RecordLockWaitTime(lockStopwatch.Elapsed.TotalMilliseconds);

            if (!lockAcquired)
            {
                _metrics?.RecordLockConflict(tenantId);
                // Another request is processing with the same key
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "Duplicate request in progress",
                    message = "A request with this idempotency key is currently being processed"
                }));
                return;
            }

            try
            {
                _metrics?.RecordNewRequest(tenantId, context.Request.Method);

                // Capture the response
                var originalBodyStream = context.Response.Body;
                using var responseBody = new MemoryStream();
                context.Response.Body = responseBody;

                await _next(context);

                // Read the response
                responseBody.Seek(0, SeekOrigin.Begin);
                var responseText = await new StreamReader(responseBody).ReadToEndAsync();

                // BUG FIX 4: Validate response size before caching
                var responseBytes = Encoding.UTF8.GetByteCount(responseText);
                if (responseBytes > _config.MaxResponseSizeBytes)
                {
                    _metrics?.RecordCacheSizeExceeded(tenantId, responseBytes);
                    _logger.LogWarning(
                        "Response too large to cache ({SizeMB:F2}MB) for idempotency key: {Key}",
                        responseBytes / 1024.0 / 1024.0,
                        idempotencyKey.ToString());

                    // Don't cache, but serve the response
                    responseBody.Seek(0, SeekOrigin.Begin);
                    await responseBody.CopyToAsync(originalBodyStream);
                    context.Response.Body = originalBodyStream;
                    return;
                }

                // BUG FIX 1: Only cache successful responses (2xx status codes)
                if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
                {
                    var record = new IdempotencyRecord
                    {
                        StatusCode = context.Response.StatusCode,
                        ContentType = context.Response.ContentType ?? "application/json",
                        Body = responseText,
                        Headers = context.Response.Headers
                            .Where(h => !h.Key.StartsWith("Transfer-", StringComparison.OrdinalIgnoreCase))
                            .ToDictionary(h => h.Key, h => h.Value.ToString()),
                        CreatedAt = DateTime.UtcNow
                    };

                    await _valkeyClient.SetAsync(cacheKey, record, _config.CacheTtl);
                    _logger.LogDebug("Cached successful response for idempotency key: {Key}", idempotencyKey.ToString());
                }
                else
                {
                    _logger.LogDebug(
                        "Skipping cache for non-success status {StatusCode} for key: {Key}",
                        context.Response.StatusCode,
                        idempotencyKey.ToString());
                }

                // RFC 9110: Signal that this is an original response (not replayed)
                context.Response.Headers["Idempotency-Replay"] = "false";

                // Copy the response to original stream
                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalBodyStream);
                context.Response.Body = originalBodyStream;
            }
            finally
            {
                // Release lock
                await _valkeyClient.DeleteAsync(lockKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in idempotency middleware for key: {IdempotencyKey}", idempotencyKey.ToString());
            // On error, proceed without idempotency protection
            await _next(context);
        }
    }

    private class IdempotencyRecord
    {
        public int StatusCode { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }
}
