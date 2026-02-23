namespace RithmTemplateApi.Configuration;

/// <summary>
/// Constants for environment variable names used throughout the application.
/// Centralizes all configuration keys to avoid magic strings and ensure consistency.
/// </summary>
/// <remarks>
/// Environment variables take precedence over appsettings.json values.
/// Use the AppSettings__ prefix for nested configuration (e.g., AppSettings__JwtSecretKey).
/// </remarks>
public static class EnvironmentVariables
{
    // =========================================================================
    // DATABASE CONNECTION STRINGS
    // =========================================================================

    /// <summary>
    /// Primary database connection string for write operations.
    /// Example: Host=localhost;Database=rithmtemplate_db;Username=sa;Password=...
    /// </summary>
    public const string DefaultConnection = "RithmDBConnectionString";

    /// <summary>
    /// Read replica database connection string for read-only operations.
    /// Falls back to DefaultConnection if not specified.
    /// </summary>
    public const string DefaultConnectionReplica = "RithmDBConnectionStringReplica";

    /// <summary>
    /// Alternative key for connection string (used in ConnectionStrings section).
    /// </summary>
    public const string DefaultContext = "DefaultContext";

    /// <summary>
    /// Alternative key for replica connection string (used in ConnectionStrings section).
    /// </summary>
    public const string DefaultContextReplica = "DefaultContextReplica";

    // =========================================================================
    // CACHE / VALKEY (REDIS)
    // =========================================================================

    /// <summary>
    /// Valkey/Redis connection string for caching and distributed locking.
    /// Example: localhost:6379,abortConnect=false
    /// </summary>
    public const string ValkeyConnection = "ValkeyConnectionString";

    /// <summary>
    /// Alternative key for Valkey connection (used in ConnectionStrings section).
    /// </summary>
    public const string ValkeyConnectionAlt = "ValkeyConnection";

    // =========================================================================
    // JWT / AUTHENTICATION
    // =========================================================================

    /// <summary>
    /// Secret key for JWT token signing. Must be at least 32 characters.
    /// </summary>
    public const string JwtSecretKey = "jwtSecretKey";

    /// <summary>
    /// JWT token issuer (who issued the token).
    /// </summary>
    public const string JwtIssuer = "JwtIssuer";

    /// <summary>
    /// JWT token audience (intended recipient).
    /// </summary>
    public const string JwtAudience = "JwtAudience";

    /// <summary>
    /// JWT token expiration time in minutes.
    /// </summary>
    public const string JwtExpirationMinutes = "JwtExpirationMinutes";

    // =========================================================================
    // SERVICE URLS (Inter-Service Communication)
    // =========================================================================

    /// <summary>
    /// AlertService base URL for SignalR notifications.
    /// </summary>
    public const string AlertServiceUrl = "AlertServiceUrl";

    /// <summary>
    /// SignalR hub URL for real-time notifications.
    /// </summary>
    public const string SignalRHubUrl = "SignalRHubUrl";

    /// <summary>
    /// UserService base URL.
    /// </summary>
    public const string UserServiceUrl = "UserServiceUrl";

    /// <summary>
    /// StationAPI base URL.
    /// </summary>
    public const string StationApiUrl = "StationApiUrl";

    /// <summary>
    /// EmailService base URL.
    /// </summary>
    public const string EmailServiceUrl = "EmailServiceUrl";

    /// <summary>
    /// WebhookService base URL.
    /// </summary>
    public const string WebhookServiceUrl = "WebhookServiceUrl";

    /// <summary>
    /// ContainerService base URL.
    /// </summary>
    public const string ContainerServiceUrl = "ContainerServiceUrl";

    /// <summary>
    /// DashboardService base URL.
    /// </summary>
    public const string DashboardServiceUrl = "DashboardServiceUrl";

    /// <summary>
    /// MapService base URL.
    /// </summary>
    public const string MapServiceUrl = "MapServiceUrl";

    // =========================================================================
    // SWAGGER / API DOCUMENTATION
    // =========================================================================

    /// <summary>
    /// Enable or disable Swagger UI. Set to "true" to enable.
    /// </summary>
    public const string EnableSwagger = "EnableSwagger";

    /// <summary>
    /// Username for Swagger UI authentication (optional).
    /// </summary>
    public const string SwaggerUsername = "SwaggerUsername";

    /// <summary>
    /// Password for Swagger UI authentication (optional).
    /// </summary>
    public const string SwaggerPassword = "SwaggerPassword";

    // =========================================================================
    // APPLICATION SETTINGS
    // =========================================================================

    /// <summary>
    /// ASP.NET Core environment name (Development, Staging, Production).
    /// </summary>
    public const string AspNetCoreEnvironment = "ASPNETCORE_ENVIRONMENT";

    /// <summary>
    /// Maximum file upload size in MB.
    /// </summary>
    public const string MaxFileSizeMB = "MaxFileSizeMB";

    /// <summary>
    /// File storage type (e.g., "linux", "minio", "azure").
    /// </summary>
    public const string FileStoreType = "FileStoreType";

    // =========================================================================
    // LOGGING
    // =========================================================================

    /// <summary>
    /// Minimum log level (Trace, Debug, Information, Warning, Error, Critical).
    /// </summary>
    public const string LogLevel = "Logging__LogLevel__Default";

    /// <summary>
    /// Log database connection string (for separate logging database).
    /// </summary>
    public const string LogDbConnectionString = "LogDBConnectionString";
}
