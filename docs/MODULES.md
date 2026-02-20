# Rithm Module System - Feature Opt-In Mechanism

## Overview

The Rithm Module System provides **configuration-driven module discovery and management** for the RithmTemplate framework. It allows developers to:

1. **Discover** all available modules and their purposes
2. **Enable/disable** modules based on project needs
3. **Validate** module dependencies at startup (fail-fast)
4. **Configure** modules via environment-specific appsettings files

**Key Benefits**:
- Self-documenting configuration with embedded metadata
- Fail-fast validation prevents invalid deployments
- Environment-specific profiles (Development vs Production)
- Zero runtime overhead (validation only at startup)
- **Visual categorization** in startup logs for quick overview

---

## Module Categories

Modules are organized into **4 categories** based on their purpose and requirements:

### 🏗️ Foundation (Required Base)

**Purpose**: Essential infrastructure that EVERY RithmXO app must have.

**Required for**:
- Multi-tenancy isolation
- Observability and monitoring
- Distributed caching (idempotency, locks)
- Health checks and graceful shutdown

**Modules**:
- ✅ **Core.Tenancy** - Multi-tenant context management (REQUIRED)
- ✅ **Core.Observability** - OpenTelemetry + Serilog (REQUIRED)
- ✅ **Core.Valkey** - Distributed cache (REQUIRED)
- ✅ **Core.Hosting** - Health checks, systemd integration (REQUIRED)

**Cannot be disabled**: These modules have `CanDisable=false`.

---

### 🔗 InterService (Service-to-Service Communication)

**Purpose**: Modules needed when your service communicates with other RithmXO services.

**When to enable**:
- Your service calls other internal services
- Your service is called by other services
- You need mTLS for secure S2S communication

**Modules**:
- ✅ **Security.Certificates** - Certificate providers (REQUIRED for mTLS)
- 🔧 **Security.MutualTLS** - Client certificate validation (enable for mTLS)
- 🔧 **Infrastructure.ServiceRouter** - Tenant-aware HTTP proxy (enable for S2S calls)

**Note**: If your service is standalone (no S2S communication), you only need Certificates (required base). Enable MutualTLS and ServiceRouter when you need inter-service communication.

---

### 🔒 Security (Advanced Authorization/Audit)

**Purpose**: Fine-grained authorization and compliance auditing.

**When to enable**:
- You need policy-based authorization beyond JWT claims
- You need audit trail for compliance (GDPR, SOC2)
- Integration with PolicyEngine and AuditAuthority

**Modules**:
- 🔧 **Security.Authorization** - Policy Engine integration (optional)
- 🔧 **Security.Audit** - AuditAuthority integration (optional)

**Dependencies**:
- Authorization requires Core.Valkey (for policy caching)
- Audit requires Core.Tenancy (for tenant isolation)

---

### 🔧 Optional (Feature-Specific)

**Purpose**: Modules for specific use cases that not all apps need.

**Modules**:
- 🔧 **Core.BatchProcessing** - Long-running operations with orphan recovery
- 🔧 **Infrastructure.SignalR** - Real-time notifications via WebSockets
- 🔧 **Infrastructure.ServiceDiscovery** - Consul integration for dynamic discovery

**When to enable**:
- BatchProcessing: If you have massive/long-running operations (bulk imports, reports)
- SignalR: If you need real-time push notifications to clients
- ServiceDiscovery: If using Consul for dynamic service discovery

---

## Module Selection by Scenario

### Scenario 1: Simple CRUD API (standalone)

**Enabled Modules**:
- 🏗️ Foundation: ALL (Tenancy, Observability, Valkey, Hosting)
- 🔗 InterService: Certificates only
- 🔒 Security: None
- 🔧 Optional: None

**Use Case**: Simple API that doesn't call other services, no advanced authz, no batch operations.

**Example Configuration** (`appsettings.json`):
```json
{
  "RithmModules": {
    "Security": {
      "MutualTLS": { "Enabled": false },
      "Authorization": { "Enabled": false },
      "Audit": { "Enabled": false }
    },
    "Infrastructure": {
      "ServiceRouter": { "Enabled": false },
      "SignalR": { "Enabled": false },
      "ServiceDiscovery": { "Enabled": false }
    }
  }
}
```

**Startup Log**:
```
═══════════════════════════════════════════════════════════
📦 RITHM MODULES - Configuration Summary
═══════════════════════════════════════════════════════════
🏗️  FOUNDATION (Required Base - ALL apps need these):
    ✅ Core.Tenancy
    ✅ Core.Observability
    ✅ Core.Valkey
    ✅ Core.Hosting
🔗 INTER-SERVICE (Service-to-Service Communication):
    ✅ Security.Certificates
⚠️  NOTE: Only Certificates enabled - service is standalone
❌ DISABLED: 6 modules
═══════════════════════════════════════════════════════════
```

---

### Scenario 2: Service with S2S Communication

**Enabled Modules**:
- 🏗️ Foundation: ALL
- 🔗 InterService: Certificates, MutualTLS, ServiceRouter
- 🔒 Security: None (unless needed)
- 🔧 Optional: None (unless needed)

**Use Case**: Service that calls other internal services with mTLS.

**Example Configuration**:
```json
{
  "RithmModules": {
    "Security": {
      "MutualTLS": { "Enabled": true }
    },
    "Infrastructure": {
      "ServiceRouter": { "Enabled": true }
    }
  }
}
```

**Startup Log**:
```
🔗 INTER-SERVICE (Service-to-Service Communication):
    ✅ Security.Certificates
    ✅ Security.MutualTLS
    ✅ Infrastructure.ServiceRouter
```

---

### Scenario 3: Service with Advanced Security

**Enabled Modules**:
- 🏗️ Foundation: ALL
- 🔗 InterService: Certificates, MutualTLS
- 🔒 Security: Authorization, Audit
- 🔧 Optional: None

**Use Case**: Service handling sensitive data with fine-grained authz and audit trail.

**Example Configuration**:
```json
{
  "RithmModules": {
    "Security": {
      "Authorization": { "Enabled": true },
      "Audit": { "Enabled": true },
      "MutualTLS": { "Enabled": true }
    }
  }
}
```

**Startup Log**:
```
🔒 SECURITY (Advanced Authorization/Audit):
    ✅ Security.Authorization
    ✅ Security.Audit
```

---

### Scenario 4: Full-Featured Service

**Enabled Modules**:
- 🏗️ Foundation: ALL
- 🔗 InterService: ALL
- 🔒 Security: ALL
- 🔧 Optional: BatchProcessing, SignalR

**Use Case**: Complex service with S2S communication, advanced security, batch operations, and real-time notifications.

**Example Configuration** (`appsettings.Production.json`):
```json
{
  "RithmModules": {
    "Core": {
      "BatchProcessing": { "Enabled": true }
    },
    "Security": {
      "Authorization": { "Enabled": true },
      "Audit": { "Enabled": true },
      "MutualTLS": { "Enabled": true }
    },
    "Infrastructure": {
      "ServiceRouter": { "Enabled": true },
      "SignalR": { "Enabled": true },
      "ServiceDiscovery": { "Enabled": true }
    }
  }
}
```

**Startup Log**:
```
═══════════════════════════════════════════════════════════
📦 RITHM MODULES - Configuration Summary
═══════════════════════════════════════════════════════════
🏗️  FOUNDATION (Required Base - ALL apps need these):
    ✅ Core.Tenancy
    ✅ Core.Observability
    ✅ Core.Valkey
    ✅ Core.Hosting
🔗 INTER-SERVICE (Service-to-Service Communication):
    ✅ Security.Certificates
    ✅ Security.MutualTLS
    ✅ Infrastructure.ServiceRouter
🔒 SECURITY (Advanced Authorization/Audit):
    ✅ Security.Authorization
    ✅ Security.Audit
🔧 OPTIONAL (Feature-specific):
    ✅ Core.BatchProcessing
    ✅ Infrastructure.SignalR
    ✅ Infrastructure.ServiceDiscovery
═══════════════════════════════════════════════════════════
```

---

## Available Modules (Detailed)

### Core Modules (Required Foundation)

These modules are **REQUIRED** and cannot be disabled (CanDisable=false).

#### Core.Tenancy
- **Description**: Multi-tenant context management
- **Project**: `Rithm.Platform.Tenancy`
- **Features**: Tenant isolation, context propagation, RLS
- **Required By**: All modules

#### Core.Observability
- **Description**: OpenTelemetry + Serilog structured logging
- **Project**: `Rithm.Platform.Observability`
- **Features**: W3C Baggage, Metrics, Tracing, PII redaction
- **Required By**: Production monitoring

#### Core.Valkey
- **Description**: Distributed cache (Redis/Valkey)
- **Project**: `Rithm.Infrastructure.Valkey`
- **Features**: Caching, distributed locks, pub/sub
- **Required By**: IdempotencyMiddleware, BatchProcessing

#### Core.Hosting
- **Description**: Health checks, graceful shutdown, systemd integration
- **Project**: `Rithm.Platform.Hosting`
- **Features**: Health endpoints, systemd sd_notify
- **Required By**: Production deployments

#### Core.BatchProcessing
- **Description**: Long-running operation tracking with orphan recovery
- **Project**: `Rithm.Infrastructure.BatchProcessing`
- **Dependencies**: Core.Valkey
- **Features**: Massive operations, progress tracking, automatic recovery
- **CanDisable**: Yes (optional for simple APIs)

### Security Modules

#### Security.Certificates (REQUIRED)
- **Description**: Certificate providers (file, RithmAuthority, systemd)
- **Project**: `Rithm.Platform.Security.Certificates`
- **Features**: mTLS certificate management
- **Providers**: RithmAuthority (production), SystemdCredentials, LocalSecureStore, None (dev)

#### Security.Authorization
- **Description**: Policy Engine integration for fine-grained authorization
- **Project**: `Rithm.Platform.Security.Authorization`
- **Dependencies**: Core.Valkey
- **Features**: Policy evaluation, caching, metrics
- **CanDisable**: Yes

#### Security.Audit
- **Description**: Audit trail via AuditAuthority service
- **Project**: `Rithm.Platform.Security.Audit`
- **Dependencies**: Core.Tenancy
- **Features**: Audit logging, compliance tracking
- **CanDisable**: Yes

#### Security.MutualTLS
- **Description**: mTLS for service-to-service authentication
- **Project**: `Rithm.Platform.Security.MutualTLS`
- **Dependencies**: Security.Certificates
- **Features**: Client certificate validation
- **CanDisable**: Yes

### Infrastructure Modules

#### Infrastructure.ServiceDiscovery
- **Description**: Service registry integration (Consul)
- **Project**: `Rithm.Platform.ServiceDiscovery`
- **Features**: Service registration, health reporting
- **CanDisable**: Yes

#### Infrastructure.SignalR
- **Description**: Real-time communication via SignalR hubs
- **Project**: `Rithm.Infrastructure.SignalR`
- **Dependencies**: Core.Tenancy
- **Features**: Progress notifications, real-time updates
- **CanDisable**: Yes

#### Infrastructure.ServiceRouter
- **Description**: HTTP proxy for tenant-aware service routing
- **Project**: `Rithm.Infrastructure.ServiceRouter`
- **Dependencies**: Core.Tenancy
- **Features**: Service-to-service routing with tenant context
- **CanDisable**: Yes

---

## Configuration Structure

### Base Configuration (appsettings.json)

The `RithmModules` section defines all available modules with their metadata:

```json
{
  "RithmModules": {
    "Core": {
      "Tenancy": {
        "Enabled": true,
        "CanDisable": false,
        "Description": "Multi-tenant context management - REQUIRED for all apps",
        "Metadata": {
          "ProjectPath": "Rithm.Platform.Tenancy",
          "RequiredBy": ["All modules"]
        }
      },
      "Valkey": {
        "Enabled": true,
        "CanDisable": false,
        "Description": "Distributed cache (Redis) - REQUIRED for idempotency and locks",
        "Metadata": {
          "ProjectPath": "Rithm.Infrastructure.Valkey",
          "RequiredBy": ["IdempotencyMiddleware", "BatchProcessing"]
        }
      },
      "BatchProcessing": {
        "Enabled": true,
        "CanDisable": true,
        "DependsOn": ["Core.Valkey"],
        "Description": "Long-running operation tracking with orphan recovery"
      }
    },
    "Security": {
      "Authorization": {
        "Enabled": false,
        "CanDisable": true,
        "DependsOn": ["Core.Valkey"],
        "Description": "Policy Engine integration for fine-grained authz"
      }
    }
  }
}
```

### Environment-Specific Overrides

#### appsettings.Development.json

Development profile with minimal modules enabled:

```json
{
  "RithmModules": {
    "Core": {
      "BatchProcessing": { "Enabled": true }
    },
    "Security": {
      "Authorization": { "Enabled": false },
      "Audit": { "Enabled": false },
      "MutualTLS": { "Enabled": false }
    },
    "Infrastructure": {
      "ServiceDiscovery": { "Enabled": false },
      "SignalR": { "Enabled": true },
      "ServiceRouter": { "Enabled": false }
    }
  }
}
```

#### appsettings.Production.json

Production profile with full security stack enabled:

```json
{
  "RithmModules": {
    "Core": {
      "BatchProcessing": { "Enabled": true }
    },
    "Security": {
      "Authorization": { "Enabled": true },
      "Audit": { "Enabled": true },
      "MutualTLS": { "Enabled": true }
    },
    "Infrastructure": {
      "ServiceDiscovery": { "Enabled": true },
      "SignalR": { "Enabled": true },
      "ServiceRouter": { "Enabled": true }
    }
  }
}
```

---

## Module Properties

### Enabled
**Type**: `bool` (default: `true`)
**Description**: Whether the module is enabled. Set to `false` to disable optional modules.

### CanDisable
**Type**: `bool` (default: `true`)
**Description**: Whether the module can be disabled. Required modules have `CanDisable=false` and will throw `ModuleValidationException` if disabled.

### DependsOn
**Type**: `string[]` (default: `[]`)
**Description**: Array of module paths this module depends on (e.g., `["Core.Valkey"]`). Validation ensures dependencies are enabled.

### Description
**Type**: `string`
**Description**: Human-readable description of the module's purpose. Visible in configuration for discoverability.

### Metadata
**Type**: `object`
**Description**: Additional descriptive information (ProjectPath, Features, RequiredBy). Not used at runtime, purely for documentation.

---

## Validation Rules

### Fail-Fast Validation

The RithmModuleBuilder performs **startup validation** and throws `ModuleValidationException` if:

1. **Missing Configuration**: `RithmModules` section not found in appsettings.json
2. **Required Module Disabled**: Attempting to disable a module with `CanDisable=false`
3. **Missing Dependency**: Enabling a module when its dependencies are disabled

### Example Validation Errors

#### Scenario 1: Missing Configuration
```
ModuleValidationException: RithmModules section not found in appsettings.json.
Add configuration to enable/disable framework modules.
```

#### Scenario 2: Disabling Required Module
```
ModuleValidationException: Module 'Core.Valkey' is required and cannot be disabled.
Set 'Enabled: true' or remove the configuration entry.
```

#### Scenario 3: Missing Dependency
```
ModuleValidationException: Module 'Core.BatchProcessing' depends on 'Core.Valkey', but 'Core.Valkey' is disabled.
Either enable 'Core.Valkey' or disable 'Core.BatchProcessing'.
```

---

## Usage

### Viewing Enabled Modules

At startup, the application logs all enabled and disabled modules:

```
[INF] 📦 Rithm Modules initialized - Enabled: Core.Tenancy, Core.Observability, Core.Valkey, Core.Hosting, Security.Certificates, Core.BatchProcessing, Infrastructure.SignalR
[INF] 📦 Rithm Modules initialized - Disabled: Security.Authorization, Security.Audit, Security.MutualTLS, Infrastructure.ServiceDiscovery, Infrastructure.ServiceRouter
```

### Enabling an Optional Module

To enable a module (e.g., Security.Authorization):

1. Edit `appsettings.json` (or environment-specific file):
   ```json
   {
     "RithmModules": {
       "Security": {
         "Authorization": {
           "Enabled": true
         }
       }
     }
   }
   ```

2. Ensure dependencies are enabled (Authorization requires Core.Valkey)

3. Restart the application - validation will confirm configuration

### Disabling an Optional Module

To disable a module (e.g., Infrastructure.SignalR):

1. Edit configuration:
   ```json
   {
     "RithmModules": {
       "Infrastructure": {
         "SignalR": {
           "Enabled": false
         }
       }
     }
   }
   ```

2. Restart - module will not be loaded

---

## Adding New Modules

### Step 1: Create Module Project

Create a new project following the Rithm naming convention:
- Core: `Rithm.Platform.{Name}`
- Security: `Rithm.Platform.Security.{Name}`
- Infrastructure: `Rithm.Infrastructure.{Name}`

### Step 2: Create Extension Method

Add a `ServiceCollectionExtensions.cs` with registration method:

```csharp
public static class NotificationsExtensions
{
    public static IServiceCollection AddRithmNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<NotificationsConfiguration>(
            configuration.GetSection("Notifications"));

        services.AddSingleton<INotificationClient, NotificationClient>();

        return services;
    }
}
```

### Step 3: Add to RithmModuleBuilder

Edit `Rithm.Platform.Core/ModuleRegistration/RithmModuleBuilder.cs`:

```csharp
public RithmModuleBuilder AddInfrastructureModules()
{
    // ... existing modules

    // Infrastructure.Notifications (OPTIONAL)
    AddModule(
        "Infrastructure.Notifications",
        register: () =>
        {
            _appBuilder.Services.AddRithmNotifications(_config);
        },
        addMiddleware: null,
        canDisable: true,
        dependencies: new[] { "Core.Tenancy" }
    );

    return this;
}
```

### Step 4: Add Configuration

Add to `appsettings.json`:

```json
{
  "RithmModules": {
    "Infrastructure": {
      "Notifications": {
        "Enabled": false,
        "CanDisable": true,
        "DependsOn": ["Core.Tenancy"],
        "Description": "Push notifications via Firebase/APNS",
        "Settings": {
          "Provider": "Firebase",
          "ApiKey": "..."
        }
      }
    }
  }
}
```

### Step 5: Test Validation

1. Try disabling dependency (should fail):
   ```json
   {
     "RithmModules": {
       "Core": { "Tenancy": { "Enabled": false } },
       "Infrastructure": { "Notifications": { "Enabled": true } }
     }
   }
   ```
   Expected: `ModuleValidationException` about missing Tenancy

2. Enable module:
   ```json
   {
     "RithmModules": {
       "Infrastructure": { "Notifications": { "Enabled": true } }
     }
   }
   ```
   Expected: Module loads successfully

**Time Estimate**: ~10-15 minutes per module

---

## Troubleshooting

### Problem: Application fails to start with ModuleValidationException

**Cause**: Invalid module configuration (missing section, disabled required module, or missing dependency)

**Solution**:
1. Read the exception message carefully - it indicates the exact issue
2. Check `RithmModules` section exists in `appsettings.json`
3. Verify all required modules are enabled:
   - Core.Tenancy
   - Core.Observability
   - Core.Valkey
   - Core.Hosting
   - Security.Certificates
4. Check dependency chains (e.g., BatchProcessing requires Valkey)

### Problem: Module is enabled but features don't work

**Cause**: Module configuration exists but actual service registration is not wired up

**Solution**:
1. Check if extension method exists (e.g., `AddRithmBatchProcessing`)
2. Verify extension method is called in `RithmModuleBuilder.Add{Category}Modules()`
3. Check Program.cs has `moduleBuilder.AddCoreModules().AddSecurityModules().AddInfrastructureModules()`

### Problem: Can't find module in configuration

**Solution**: All modules are documented in base `appsettings.json`. Check the `RithmModules` section for the complete list with descriptions and metadata.

---

## Architecture Notes

### Module Paths

Modules are referenced using dotted paths: `{Category}.{ModuleName}`

Examples:
- `Core.Tenancy`
- `Security.Authorization`
- `Infrastructure.SignalR`

### Validation Flow

```
Application Startup
    ↓
LoadModuleConfiguration()
    ↓ (parse RithmModules section)
AddCoreModules()
AddSecurityModules()
AddInfrastructureModules()
    ↓ (for each module: check CanDisable, validate dependencies)
Validate()
    ↓ (ensure required modules enabled)
BuildMiddlewarePipeline()
    ↓
Application Ready
```

### Configuration Merging

ASP.NET Core merges configurations in this order:
1. `appsettings.json` (base)
2. `appsettings.{Environment}.json` (overrides)
3. Environment variables (highest priority)

Only properties explicitly set in environment-specific files override the base. Omitted properties inherit from base configuration.

---

## Future Enhancements

### Potential Improvements (Out of Scope)

1. **Module Auto-Discovery**: Scan assemblies for modules (adds reflection overhead)
2. **Hot Reload**: Change modules without restart (complex, requires service provider recreation)
3. **Admin UI**: Web interface to toggle modules (nice-to-have)
4. **Telemetry**: Track module usage via metrics (trivial to add)
5. **Module Versioning**: Track module versions for compatibility (useful for large teams)

### Incremental Migration Path

The current implementation provides:
- ✅ Configuration-driven module discovery
- ✅ Fail-fast validation
- ✅ Dependency checking
- ⏳ Incremental service registration migration (ongoing)

Future phases will migrate inline service registrations in `Program.cs` to use module builder exclusively. Current approach allows gradual migration without breaking existing functionality.

---

## References

- **Project**: `Rithm.Platform.Core` - Core module system infrastructure
- **Configuration**: `appsettings.json` - Base module configuration
- **Validation**: `ModuleValidationException` - Fail-fast error reporting
- **Builder**: `RithmModuleBuilder` - Fluent API for module registration
- **Plan**: `/Users/mauriciofigueroa/.claude/plans/functional-hopping-comet.md` - Original implementation plan

---

## Quick Reference

### Enable Module
```json
{ "RithmModules": { "Security": { "Authorization": { "Enabled": true } } } }
```

### Disable Module (if CanDisable=true)
```json
{ "RithmModules": { "Infrastructure": { "SignalR": { "Enabled": false } } } }
```

### Check Required Modules
Required modules (CanDisable=false):
- Core.Tenancy
- Core.Observability
- Core.Valkey
- Core.Hosting
- Security.Certificates

### View Enabled Modules
Check application startup logs for:
```
[INF] 📦 Rithm Modules initialized - Enabled: ...
```
