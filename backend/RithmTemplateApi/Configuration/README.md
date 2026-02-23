# Configuration

Centralized configuration management with environment variable support.

## Purpose

This folder contains:

- **Environment Variable Constants**: Single source of truth for all configuration keys
- **Configuration Extensions**: Helper methods for reading settings with environment variable priority
- **Consistent Patterns**: Aligns with Rithm's existing `ConfigHelpers.GetAppSettingValue()` pattern

## Components

### EnvironmentVariables.cs

Static class containing all environment variable names as constants.

```csharp
// Instead of magic strings:
var connStr = config["RithmDBConnectionString"]; // ❌ Magic string

// Use constants:
var connStr = config.GetAppSettingValue(EnvironmentVariables.DefaultConnection); // ✅ Type-safe
```

### ConfigurationExtensions.cs

Extension methods for `IConfiguration` that provide environment variable priority.

## Configuration Resolution Order

When reading a setting, the following sources are checked in order:

| Priority | Source | Example Key |
|----------|--------|-------------|
| 1 | Environment variable (exact) | `RithmDBConnectionString` |
| 2 | Environment variable (prefixed) | `AppSettings__RithmDBConnectionString` |
| 3 | appsettings.json AppSettings section | `"AppSettings": { "RithmDBConnectionString": "..." }` |
| 4 | appsettings.json root level | `"RithmDBConnectionString": "..."` |

For connection strings:

| Priority | Source | Example Key |
|----------|--------|-------------|
| 1 | App setting key | `RithmDBConnectionString` |
| 2 | Environment variable (prefixed) | `ConnectionStrings__DefaultContext` |
| 3 | ConnectionStrings section | `"ConnectionStrings": { "DefaultContext": "..." }` |

## Usage Examples

### Reading Configuration Values

```csharp
using RithmTemplateApi.Configuration;

public class MyService
{
    private readonly IConfiguration _config;

    public MyService(IConfiguration config)
    {
        _config = config;
    }

    public void DoSomething()
    {
        // String value
        var jwtKey = _config.GetAppSettingValue(EnvironmentVariables.JwtSecretKey);

        // With default
        var issuer = _config.GetAppSettingValue(EnvironmentVariables.JwtIssuer, "RithmTemplate");

        // Boolean value
        var enableSwagger = _config.GetAppSettingBool(EnvironmentVariables.EnableSwagger, true);

        // Integer value
        var maxFileSize = _config.GetAppSettingInt(EnvironmentVariables.MaxFileSizeMB, 100);

        // Database connection string
        var connectionString = _config.GetDatabaseConnectionString(
            EnvironmentVariables.DefaultConnection,
            EnvironmentVariables.DefaultContext);
    }
}
```

### In Program.cs / Startup

```csharp
using RithmTemplateApi.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Get connection string with env var priority
var connectionString = builder.Configuration.GetDatabaseConnectionString(
    EnvironmentVariables.DefaultConnection,
    EnvironmentVariables.DefaultContext);

// Configure JWT
var jwtKey = builder.Configuration.GetAppSettingValue(
    EnvironmentVariables.JwtSecretKey,
    "default-development-key");
```

## Environment Variable Categories

### Database

| Variable | Description | Example |
|----------|-------------|---------|
| `RithmDBConnectionString` | Primary DB (write) | `Host=localhost;Database=...` |
| `RithmDBConnectionStringReplica` | Read replica DB | `Host=replica;Database=...` |
| `ValkeyConnectionString` | Redis/Valkey cache | `localhost:6379` |

### Authentication

| Variable | Description | Example |
|----------|-------------|---------|
| `jwtSecretKey` | JWT signing key (32+ chars) | `your-super-secret-key-here` |
| `JwtIssuer` | Token issuer | `RithmTemplate` |
| `JwtAudience` | Token audience | `RithmTemplateUsers` |

### Service URLs

| Variable | Description | Default |
|----------|-------------|---------|
| `AlertServiceUrl` | AlertService base URL | `http://localhost:5100` |
| `UserServiceUrl` | UserService base URL | `http://localhost:5000` |
| `SignalRHubUrl` | SignalR hub endpoint | `http://localhost:5100/hubs/alerts` |

### Application Settings

| Variable | Description | Default |
|----------|-------------|---------|
| `EnableSwagger` | Enable Swagger UI | `true` (dev) |
| `MaxFileSizeMB` | Max upload size | `100` |
| `FileStoreType` | Storage backend | `linux` |

## Setting Environment Variables

### Linux / macOS

```bash
# Current session
export RithmDBConnectionString="Host=localhost;Database=mydb;Username=sa;Password=secret"

# Permanent (add to ~/.bashrc or ~/.zshrc)
echo 'export RithmDBConnectionString="..."' >> ~/.bashrc
```

### Windows

```powershell
# Current session
$env:RithmDBConnectionString = "Host=localhost;Database=mydb;Username=sa;Password=secret"

# Permanent (Machine level - requires admin)
[Environment]::SetEnvironmentVariable("RithmDBConnectionString", "...", "Machine")

# Permanent (User level)
[Environment]::SetEnvironmentVariable("RithmDBConnectionString", "...", "User")
```

### Docker / Kubernetes

```yaml
# docker-compose.yml
services:
  api:
    environment:
      - RithmDBConnectionString=Host=db;Database=mydb;Username=sa;Password=secret
      - jwtSecretKey=your-secret-key

# Kubernetes ConfigMap/Secret
apiVersion: v1
kind: Secret
metadata:
  name: rithmtemplate-secrets
data:
  RithmDBConnectionString: <base64-encoded>
  jwtSecretKey: <base64-encoded>
```

## Best Practices

1. **Never hardcode secrets** - Always use environment variables for sensitive data
2. **Use constants** - Reference `EnvironmentVariables` class instead of magic strings
3. **Provide defaults** - Use fallback values for non-critical settings
4. **Document new variables** - Add new constants to `EnvironmentVariables.cs` with XML docs
5. **Validate required settings** - Check for required settings at startup and fail fast
