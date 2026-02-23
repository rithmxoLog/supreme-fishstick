namespace RithmTemplateApi.Middleware.Configuration;

/// <summary>
/// Configuration for idempotency middleware.
/// Controls cache TTL, validation limits, and locking behavior.
/// </summary>
public class IdempotencyConfiguration
{
    /// <summary>
    /// Time-to-live for idempotency cache (default: 24 hours).
    /// Cached responses are kept for this duration.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Time-to-live for idempotency processing locks (default: 30 seconds).
    /// Prevents concurrent processing of the same idempotency key.
    /// </summary>
    public TimeSpan LockTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum length for idempotency keys (default: 255 characters).
    /// Keys exceeding this length will be rejected with 400 Bad Request.
    /// </summary>
    public int MaxKeyLength { get; set; } = 255;

    /// <summary>
    /// Maximum response body size to cache in bytes (default: 5MB).
    /// Responses larger than this will not be cached.
    /// </summary>
    public int MaxResponseSizeBytes { get; set; } = 5 * 1024 * 1024; // 5MB

    /// <summary>
    /// Gets the cache TTL in hours (for appsettings.json binding).
    /// </summary>
    public double CacheTtlHours
    {
        get => CacheTtl.TotalHours;
        set => CacheTtl = TimeSpan.FromHours(value);
    }

    /// <summary>
    /// Gets the lock TTL in seconds (for appsettings.json binding).
    /// </summary>
    public double LockTtlSeconds
    {
        get => LockTtl.TotalSeconds;
        set => LockTtl = TimeSpan.FromSeconds(value);
    }

    /// <summary>
    /// Gets the max response size in megabytes (for appsettings.json binding).
    /// </summary>
    public double MaxResponseSizeMB
    {
        get => MaxResponseSizeBytes / 1024.0 / 1024.0;
        set => MaxResponseSizeBytes = (int)(value * 1024 * 1024);
    }
}
