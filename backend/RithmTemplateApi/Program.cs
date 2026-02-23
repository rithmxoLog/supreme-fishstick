using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RithmTemplateApi.Configuration;
using RithmTemplateApi.Middleware;
using RithmTemplateApi.Services;
using Rithm.Infrastructure.BatchProcessing;
using Rithm.Infrastructure.SignalR;
using Rithm.Infrastructure.Valkey;
using Rithm.Infrastructure.ServiceRouter;
using Rithm.Platform.Tenancy;
using RithmTemplate.DAL.Persistence;
using HealthChecks.UI.Client;
using HealthChecks.NpgSql;
using System.Text;
using FluentValidation;
using MediatR;
using RithmTemplate.Application.Common.Abstractions;
using RithmTemplate.Application.Common.Behaviors;
using RithmTemplate.Application.BatchProcessing.Orchestrators;
using Rithm.Platform.ServiceDiscovery;
using Rithm.Platform.ServiceDiscovery.Models;
using Rithm.Platform.Security.Certificates;
using Rithm.Platform.Security.Authorization;
using Rithm.Platform.Security.Authorization.Configuration;
using Rithm.Platform.Security.Authorization.Metrics;
using Rithm.Platform.Security.Audit;
using Rithm.Platform.Security.Audit.Configuration;
using Rithm.Platform.Security.Audit.Metrics;
using Rithm.Infrastructure.BatchProcessing.Metrics;
using RithmTemplateApi.HealthChecks;
using Serilog;
using RithmTemplateApi.Observability;
using Rithm.Platform.Core.ModuleRegistration;
using RithmTemplateApi.XoPublic.Configuration;
using RithmTemplateApi.XoPublic.Services;
using RithmTemplateApi.XoPublic.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// =============================================================================
// RITHM MODULE SYSTEM - Configuration-driven module discovery and validation
// =============================================================================

// Initialize RithmModuleBuilder - validates RithmModules configuration
// and provides fail-fast feedback on missing dependencies or invalid config
var moduleBuilder = new RithmModuleBuilder(builder)
    .AddCoreModules()
    .AddSecurityModules()
    .AddInfrastructureModules()
    .AddIntegrationModules()
    .AddMediatRBehaviors()
    .Validate();

// Log modules by category for better visibility
var enabledModules = moduleBuilder.GetEnabledModules();
var modulesByCategory = moduleBuilder.GetModulesByCategory();

Log.Information("═══════════════════════════════════════════════════════════");
Log.Information("📦 RITHM MODULES - Configuration Summary");
Log.Information("═══════════════════════════════════════════════════════════");

if (modulesByCategory["Foundation"].Any())
{
    Log.Information("🏗️  FOUNDATION (Required Base - ALL apps need these):");
    foreach (var module in modulesByCategory["Foundation"])
    {
        Log.Information("    ✅ {Module}", module);
    }
}

if (modulesByCategory["InterService"].Any())
{
    Log.Information("🔗 INTER-SERVICE (Service-to-Service Communication):");
    foreach (var module in modulesByCategory["InterService"])
    {
        Log.Information("    ✅ {Module}", module);
    }
}
else
{
    Log.Warning("⚠️  INTER-SERVICE: No modules enabled (service isolated - no S2S communication)");
}

if (modulesByCategory["Security"].Any())
{
    Log.Information("🔒 SECURITY (Advanced Authorization/Audit):");
    foreach (var module in modulesByCategory["Security"])
    {
        Log.Information("    ✅ {Module}", module);
    }
}

if (modulesByCategory["Integration"].Any())
{
    Log.Information("🔄 INTEGRATION (Cross-service data exchange):");
    foreach (var module in modulesByCategory["Integration"])
    {
        Log.Information("    ✅ {Module}", module);
    }
}

if (modulesByCategory["Optional"].Any())
{
    Log.Information("🔧 OPTIONAL (Feature-specific):");
    foreach (var module in modulesByCategory["Optional"])
    {
        Log.Information("    ✅ {Module}", module);
    }
}

// Show disabled modules (important for troubleshooting)
var disabledModules = enabledModules.Where(m => !m.Value).Select(m => m.Key).ToList();
if (disabledModules.Any())
{
    Log.Information("❌ DISABLED: {DisabledCount} modules", disabledModules.Count);
    foreach (var module in disabledModules)
    {
        Log.Debug("    - {Module}", module);
    }
}

Log.Information("═══════════════════════════════════════════════════════════");

// Enable Systemd integration (sd_notify lifecycle, journal logging)
// Ignored when not running under systemd (e.g., Windows/Development)
builder.Host.UseSystemd();

// =============================================================================
// CONFIGURATION - Environment variable priority
// =============================================================================

// Get configuration with environment variable priority
var config = builder.Configuration;

// Database connection string (env var takes priority)
var dbConnectionString = config.GetDatabaseConnectionString(
    EnvironmentVariables.DefaultConnection,
    EnvironmentVariables.DefaultContext) ?? "";

// JWT settings (env var takes priority)
var jwtKey = config.GetAppSettingValue(EnvironmentVariables.JwtSecretKey, "default-key-for-development-only-32chars");
var jwtIssuer = config.GetAppSettingValue(EnvironmentVariables.JwtIssuer, "RithmTemplate");
var jwtAudience = config.GetAppSettingValue(EnvironmentVariables.JwtAudience, "RithmTemplateUsers");

// Feature flags
var enableSwagger = config.GetAppSettingBool(EnvironmentVariables.EnableSwagger, builder.Environment.IsDevelopment());

// =============================================================================
// SERVICES CONFIGURATION
// =============================================================================

// Health Checks with InfraSoT connectivity
var healthChecksBuilder = builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy())
    .AddNpgSql(
        dbConnectionString,
        name: "postgresql",
        tags: new[] { "db", "sql", "postgresql" })
    .AddCheck<InfraSoTHealthCheck>(
        "infrasot_connectivity",
        tags: new[] { "infrasot" });

// XoPublic Conformity Health Check (Pilar 5 — only if Integration.XoPublic module is enabled)
var xoPublicEnabled = moduleBuilder.IsModuleEnabled("Integration.XoPublic");
if (xoPublicEnabled)
{
    healthChecksBuilder.AddCheck<XoPublicConformityHealthCheck>(
        "xo_public_conformity",
        tags: new[] { "xo_public", "integration" });
}

// Exception Handler (Modern .NET 8 approach)
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// OpenTelemetry Instrumentation (Distributed Tracing + Metrics)
builder.Services.AddOpenTelemetryInstrumentation(builder.Configuration);

// Tenant Context (scoped per request) + HttpContextAccessor for DbContext
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<Rithm.Platform.Tenancy.Baggage.IBaggageContext, Rithm.Platform.Tenancy.Baggage.BaggageContext>();
Log.Information("✅ W3C Baggage context propagation configured");
builder.Services.AddHttpContextAccessor();

// =============================================================================
// INFRASOT & MTLS CONFIGURATION
// =============================================================================

// InfraSoT Bootstrap Configuration
builder.Services.Configure<InfraSoTBootstrapConfig>(builder.Configuration.GetSection("InfraSoT"));

// Certificate Configuration
builder.Services.Configure<CertificateConfiguration>(builder.Configuration.GetSection("Certificate"));

// Mutual TLS Configuration
builder.Services.Configure<MutualTlsConfiguration>(builder.Configuration.GetSection("MutualTLS"));

// Certificate Provider (RithmAuthority for production, LocalSecureStore for contingency, None for development)
// rithmXO Architecture: Certificates obtained from Service Identity Authority (NOT third-party services)
var certProviderType = builder.Configuration["Certificate:ProviderType"] ?? "None";

Log.Information("Configuring certificate provider: {ProviderType}", certProviderType);

switch (certProviderType.ToLowerInvariant())
{
    case "rithmauthority":
        // PRODUCTION STANDARD: Obtain certificates from Service Identity Authority via InfraSoT
        builder.Services.AddSingleton<ICertificateProvider, RithmAuthorityCertificateProvider>();
        Log.Information("✅ RithmAuthority certificate provider configured (Service Identity Authority)");
        break;

    case "systemdcredentials":
        // PRODUCTION ALTERNATIVE: Load certificates from systemd LoadCredential
        builder.Services.AddSingleton<ICertificateProvider, SystemdCredentialsCertificateProvider>();
        Log.Information("✅ SystemdCredentials certificate provider configured (systemd LoadCredential)");
        break;

    case "localsecurestore":
        // CONTINGENCY: Read from tmpfs (manual deployment)
        builder.Services.AddSingleton<ICertificateProvider, LocalSecureStoreCertificateProvider>();
        Log.Warning("⚠️ LocalSecureStore certificate provider configured - CONTINGENCY MODE");
        break;

    case "none":
        // DEVELOPMENT: No mTLS
        builder.Services.AddSingleton<ICertificateProvider, NoCertificateProvider>();
        Log.Warning("⚠️ No certificate provider - DEVELOPMENT MODE (no mTLS)");
        break;

    default:
        throw new InvalidOperationException(
            $"Invalid certificate provider type: {certProviderType}. " +
            "Valid options: RithmAuthority (production), SystemdCredentials (production alternative), " +
            "LocalSecureStore (contingency), None (development).");
}

// InfraSoT HTTP Client with mTLS and caching
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<IInfraSoTClient, InfraSoTClient>("InfraSoT")
    .ConfigurePrimaryHttpMessageHandler((serviceProvider) =>
    {
        var certificateProvider = serviceProvider.GetService<ICertificateProvider>();
        var handler = new HttpClientHandler();

        // Configure mTLS if certificate provider is available and not NoCertificateProvider
        if (certificateProvider != null && certificateProvider.GetType() != typeof(NoCertificateProvider))
        {
            try
            {
                var clientCert = certificateProvider.GetClientCertificateAsync().GetAwaiter().GetResult();
                handler.ClientCertificates.Add(clientCert);

                // Custom server certificate validation
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                    certificateProvider.ValidateClientCertificate(cert, chain, errors);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogWarning(ex, "Failed to configure client certificate for InfraSoT. Proceeding without mTLS.");
            }
        }

        return handler;
    });

// InfraSoT Registration Background Service
var enableInfraSoTRegistration = builder.Configuration.GetValue<bool>("InfraSoT:EnableRegistration", true);
if (enableInfraSoTRegistration)
{
    builder.Services.AddHostedService<InfraSoTRegistrationService>();
}

// Graceful Shutdown Handler (systemd integration)
builder.Services.AddHostedService<Rithm.Platform.Hosting.GracefulShutdownHandler>();

// Orphaned Operation Recovery Worker
builder.Services.AddHostedService<Rithm.Infrastructure.BatchProcessing.OrphanedOperationRecoveryWorker>();

// Swagger/OpenAPI
if (enableSwagger)
{
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "RithmTemplate API",
            Version = "v1",
            Description = "Rithm Template Microservice API"
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });
}

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// =============================================================================
// DEPENDENCY INJECTION - Application Services
// =============================================================================

// Valkey/Redis Client
builder.Services.AddSingleton<IValkeyClient, ValkeyClient>();

// Metrics
builder.Services.AddSingleton<Rithm.Infrastructure.Valkey.Metrics.ValkeyMetrics>();
builder.Services.AddSingleton<RithmTemplateApi.Metrics.IdempotencyMetrics>();
Log.Information("✅ Metrics configured: ValkeyMetrics, IdempotencyMetrics");

// Distributed State Configuration
builder.Services.Configure<Rithm.Infrastructure.BatchProcessing.DistributedStateConfiguration>(
    builder.Configuration.GetSection("DistributedState"));

// Idempotency Configuration
builder.Services.Configure<RithmTemplateApi.Middleware.Configuration.IdempotencyConfiguration>(
    builder.Configuration.GetSection("Idempotency"));

// =============================================================================
// POLICY ENGINE & AUDIT AUTHORITY
// =============================================================================

// Policy Engine Configuration
builder.Services.Configure<PolicyEngineConfiguration>(
    builder.Configuration.GetSection("PolicyEngine"));

// Audit Authority Configuration
builder.Services.Configure<AuditAuthorityConfiguration>(
    builder.Configuration.GetSection("AuditAuthority"));

// Policy Decision Context (scoped per request)
builder.Services.AddScoped<IPolicyDecisionContext, PolicyDecisionContext>();

// Policy Engine HTTP Client
builder.Services.AddHttpClient<IPolicyEngineClient, PolicyEngineClient>("PolicyEngine");

// Audit Authority HTTP Client
builder.Services.AddHttpClient<IAuditAuthorityClient, AuditAuthorityClient>("AuditAuthority");

// Observability Metrics (singleton for performance)
builder.Services.AddSingleton<PolicyEngineMetrics>();
builder.Services.AddSingleton<AuditAuthorityMetrics>();
builder.Services.AddSingleton<MassiveOperationMetrics>();

// SignalR Progress Notifier
builder.Services.AddSingleton<IProgressNotifier, SignalRNotificationClient>();

// Batch Processing / Massive Operations
builder.Services.AddSingleton<IMassiveOperationManager, MassiveOperationManager>();

// Service Router with Tenant Context Propagation and InfraSoT
builder.Services.AddHttpClient<IServiceRouterClient, ServiceRouterClient>("ServiceRouter")
    .AddHttpMessageHandler<TenantContextPropagationHandler>();

builder.Services.AddTransient<TenantContextPropagationHandler>();

// Database Context (uses environment variable priority internally)
builder.Services.AddInfrastructureData(builder.Configuration);

// =============================================================================
// XO PUBLIC INTEGRATION (Pilar 1-6: IntegratorXO Framework)
// =============================================================================

if (xoPublicEnabled)
{
    builder.Services.Configure<XoPublicConfiguration>(builder.Configuration.GetSection("XoPublic"));
    builder.Services.AddScoped<IXoPublicSchemaDiscoveryService, XoPublicSchemaDiscoveryService>();
    Log.Information("✅ XoPublic integration module enabled (xo_public schema, metadata endpoint, conformity check)");
}

// =============================================================================
// MEDIATR & CQRS - Application Layer
// =============================================================================

// MediatR with pipeline behaviors (order matters: first registered = outermost)
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(ICommand).Assembly);

    // Pipeline order (outermost to innermost):
    // 1. AuditBehavior (OUTER - wraps all for audit trail)
    // 2. LoggingBehavior (request logging and correlation)
    // 3. AuthorizationBehavior (policy enforcement BEFORE validation)
    // 4. ValidationBehavior (input validation)
    // 5. PerformanceBehavior (performance monitoring)
    // 6. Handler (business logic)

    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
});

// FluentValidation validators (auto-discovered from Helpers assembly)
builder.Services.AddValidatorsFromAssembly(typeof(ICommand).Assembly);

// Orchestrators (for batch/long-running operations)
builder.Services.AddScoped<SampleBulkImportOrchestrator>();

// =============================================================================
// SERILOG CONFIGURATION (Structured Logging)
// =============================================================================

// Configure Serilog with ecosystem context enrichment via LogContext
// PII redaction enricher for GDPR/CCPA compliance
var piiConfig = builder.Configuration.GetSection("Serilog:PiiRedaction").Get<Rithm.Platform.Observability.Serilog.PiiRedactionConfiguration>()
    ?? Rithm.Platform.Observability.Serilog.PiiRedactionConfiguration.Default;

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.With(new Rithm.Platform.Observability.Serilog.PiiRedactionEnricher(piiConfig)));

var app = builder.Build();

// =============================================================================
// MIDDLEWARE PIPELINE
// =============================================================================

// Serilog Request Logging (captures HTTP request details)
app.UseSerilogRequestLogging();

// Exception Handler (must be first)
app.UseExceptionHandler();

// Swagger (controlled by EnableSwagger setting)
if (enableSwagger)
{
    app.UseMiddleware<SwaggerAuthorizationMiddleware>();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "RithmTemplate API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "RithmTemplate API - INTERNAL";
    });
}

// CORS
app.UseCors("AllowAll");

// Ecosystem Identity Middleware (CRITICAL: Extract tenant context before authentication)
app.UseMiddleware<EcosystemIdentityMiddleware>();

// Logging Scope Middleware (pushes ecosystem context into Serilog LogContext)
app.UseMiddleware<LoggingScopeMiddleware>();

// Policy Decision Header Middleware (adds X-Policy-Decision-Id to responses)
app.UseMiddleware<PolicyDecisionHeaderMiddleware>();

// Mutual TLS Validation Middleware (after identity, before idempotency)
var enableMtls = builder.Configuration.GetValue<bool>("MutualTLS:Enabled", false);
if (enableMtls)
{
    app.UseMiddleware<MutualTlsValidationMiddleware>();
}

// Idempotency Middleware (after tenant context is set)
app.UseMiddleware<IdempotencyMiddleware>();

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Controllers
app.MapControllers();

// =============================================================================
// HEALTH CHECK ENDPOINTS (Portcullis-compliant)
// =============================================================================

var appId = builder.Configuration["InfraSoT:AppId"] ?? "rithm-template-service";
var version = builder.Configuration["InfraSoT:Version"] ?? "1.0.0";
var environment = builder.Configuration["InfraSoT:Environment"] ?? "development";

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false, // No checks, just confirms app is running
    ResponseWriter = (context, report) =>
        PortcullisHealthResponseWriter.WriteResponseAsync(context, report, appId, version, environment)
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true, // All checks
    ResponseWriter = (context, report) =>
        PortcullisHealthResponseWriter.WriteResponseAsync(context, report, appId, version, environment)
});

// Metrics endpoint (placeholder - integrate with Prometheus/OpenTelemetry)
app.MapGet("/metrics", () => Results.Ok(new
{
    timestamp = DateTime.UtcNow,
    uptime = Environment.TickCount64,
    environment = app.Environment.EnvironmentName
}));

app.Run();
