using System.Runtime.InteropServices;

namespace RithmTemplateApi.Configuration;

/// <summary>
/// Extension methods for IConfiguration to read settings with environment variable priority.
/// Mirrors the behavior of Rithm's ConfigHelpers.GetAppSettingValue() for consistency.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Gets a configuration value with environment variable priority.
    /// Resolution order:
    /// 1. Environment variable with exact name
    /// 2. Environment variable with AppSettings__ prefix
    /// 3. AppSettings section in configuration
    /// 4. Root level in configuration
    /// </summary>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="key">The configuration key to retrieve.</param>
    /// <returns>The configuration value or null if not found.</returns>
    public static string? GetAppSettingValue(this IConfiguration configuration, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        // 1. Check environment variable directly
        var envValue = GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        // 2. Check environment variable with AppSettings__ prefix
        var prefixedKey = $"AppSettings__{key}";
        envValue = GetEnvironmentVariable(prefixedKey);
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        // 3. Check AppSettings section in configuration
        var appSettingsValue = configuration[$"AppSettings:{key}"];
        if (!string.IsNullOrWhiteSpace(appSettingsValue))
            return appSettingsValue;

        // 4. Check root level in configuration
        var rootValue = configuration[key];
        if (!string.IsNullOrWhiteSpace(rootValue))
            return rootValue;

        return null;
    }

    /// <summary>
    /// Gets a configuration value with a default fallback.
    /// </summary>
    public static string GetAppSettingValue(this IConfiguration configuration, string key, string defaultValue)
    {
        return configuration.GetAppSettingValue(key) ?? defaultValue;
    }

    /// <summary>
    /// Gets a database connection string with environment variable priority.
    /// Resolution order:
    /// 1. Environment variable with the key name
    /// 2. Environment variable with ConnectionStrings__ prefix
    /// 3. ConnectionStrings section in configuration
    /// </summary>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="appSettingKey">The app setting key (e.g., "RithmDBConnectionString").</param>
    /// <param name="connectionStringKey">The connection string key fallback (e.g., "DefaultContext").</param>
    /// <returns>The connection string or null if not found.</returns>
    public static string? GetDatabaseConnectionString(
        this IConfiguration configuration,
        string appSettingKey,
        string? connectionStringKey = null)
    {
        // 1. Try app setting key (environment variable priority)
        var connectionString = configuration.GetAppSettingValue(appSettingKey);
        if (!string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        // 2. Try connection string with environment variable
        if (!string.IsNullOrWhiteSpace(connectionStringKey))
        {
            var envKey = $"ConnectionStrings__{connectionStringKey}";
            connectionString = GetEnvironmentVariable(envKey);
            if (!string.IsNullOrWhiteSpace(connectionString))
                return connectionString;

            // 3. Try ConnectionStrings section
            connectionString = configuration.GetConnectionString(connectionStringKey);
            if (!string.IsNullOrWhiteSpace(connectionString))
                return connectionString;
        }

        return null;
    }

    /// <summary>
    /// Gets a typed configuration value.
    /// </summary>
    public static T? GetAppSettingValue<T>(this IConfiguration configuration, string key) where T : struct
    {
        var value = configuration.GetAppSettingValue(key);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets a typed configuration value with default.
    /// </summary>
    public static T GetAppSettingValue<T>(this IConfiguration configuration, string key, T defaultValue) where T : struct
    {
        return configuration.GetAppSettingValue<T>(key) ?? defaultValue;
    }

    /// <summary>
    /// Gets a boolean configuration value. Supports "true", "1", "yes" as true values.
    /// </summary>
    public static bool GetAppSettingBool(this IConfiguration configuration, string key, bool defaultValue = false)
    {
        var value = configuration.GetAppSettingValue(key);
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.Ordinal) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets an integer configuration value.
    /// </summary>
    public static int GetAppSettingInt(this IConfiguration configuration, string key, int defaultValue = 0)
    {
        var value = configuration.GetAppSettingValue(key);
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// Retrieves an environment variable, handling platform-specific considerations.
    /// On Windows, checks Machine, User, and Process scopes.
    /// On Linux/macOS, checks Process scope only.
    /// </summary>
    private static string? GetEnvironmentVariable(string name)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: Check Machine -> User -> Process
            var value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        // All platforms: Check Process
        return Environment.GetEnvironmentVariable(name);
    }
}
