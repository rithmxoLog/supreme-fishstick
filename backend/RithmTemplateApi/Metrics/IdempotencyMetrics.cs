using System.Diagnostics.Metrics;

namespace RithmTemplateApi.Metrics;

/// <summary>
/// Provides OpenTelemetry metrics for idempotency middleware operations.
/// Tracks cache hits, new requests, lock conflicts, and cache size limits.
/// </summary>
public class IdempotencyMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _newRequests;
    private readonly Counter<long> _lockConflicts;
    private readonly Counter<long> _cacheSizeExceeded;
    private readonly Histogram<double> _lockWaitTime;

    public IdempotencyMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create("RithmTemplate.Idempotency");

        _cacheHits = _meter.CreateCounter<long>(
            "idempotency.cache.hits",
            description: "Requests served from idempotency cache");

        _newRequests = _meter.CreateCounter<long>(
            "idempotency.requests.new",
            description: "New requests processed");

        _lockConflicts = _meter.CreateCounter<long>(
            "idempotency.lock.conflicts",
            description: "Lock conflicts (409 responses)");

        _cacheSizeExceeded = _meter.CreateCounter<long>(
            "idempotency.cache.size_exceeded",
            description: "Responses too large to cache");

        _lockWaitTime = _meter.CreateHistogram<double>(
            "idempotency.lock.wait_time",
            unit: "ms",
            description: "Time spent waiting for lock");
    }

    public void RecordCacheHit(string tenantId, string httpMethod) =>
        _cacheHits.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("http_method", httpMethod));

    public void RecordNewRequest(string tenantId, string httpMethod) =>
        _newRequests.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("http_method", httpMethod));

    public void RecordLockConflict(string tenantId) =>
        _lockConflicts.Add(1, new KeyValuePair<string, object?>("tenant_id", tenantId));

    public void RecordCacheSizeExceeded(string tenantId, long responseSizeBytes) =>
        _cacheSizeExceeded.Add(1,
            new KeyValuePair<string, object?>("tenant_id", tenantId),
            new KeyValuePair<string, object?>("response_size_bytes", responseSizeBytes));

    public void RecordLockWaitTime(double waitTimeMs) =>
        _lockWaitTime.Record(waitTimeMs);
}
