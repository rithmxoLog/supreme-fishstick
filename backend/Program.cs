using System.Text;
using System.Threading.RateLimiting;
using GitXO.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var appPort = Environment.GetEnvironmentVariable("APP_PORT") ?? "3001";
builder.WebHost.UseUrls($"http://localhost:{appPort}");

// Raise Kestrel's hard body-size cap so large ZIP/push uploads aren't
// connection-reset before the per-endpoint [RequestSizeLimit] filter runs.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 200 * 1024 * 1024);

// Raise the multipart-form parser limit (default 128 MB) to match.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = 200 * 1024 * 1024);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p => p
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader()));

// JWT Authentication
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSecret = jwtSection["Secret"] ?? "gitxo-default-secret-change-this-now-32chars";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"] ?? "GitXO",
            ValidAudience = jwtSection["Audience"] ?? "GitXO",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

// Rate limiting: max 10 requests per IP per 5 minutes on auth endpoints
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", httpCtx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpCtx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));
    options.RejectionStatusCode = 429;
});
builder.Services.AddSingleton<ActivityLogger>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<RepoDbService>();

// Resolve and ensure repository directories exist

static string ResolveDir(IConfiguration cfg, string key, string fallback)
{
    // Use Addy's writeable data directory
    var dataDir = Environment.GetEnvironmentVariable("RITHM_DATA_DIR");

    if (string.IsNullOrWhiteSpace(dataDir))
    {
        // local development fallback
        dataDir = Path.Combine(AppContext.BaseDirectory, "..", "data");
    }

    // All repo paths MUST go under the writable data dir
    var finalPath = Path.Combine(dataDir, fallback);

    Directory.CreateDirectory(finalPath);

    // update config so the app prints correct paths
    cfg[key] = finalPath;

    return finalPath;
}


var reposDir        = ResolveDir(builder.Configuration, "ReposDirectory",        "repositories");
var privateReposDir = ResolveDir(builder.Configuration, "PrivateReposDirectory", "repositories-private");

var app = builder.Build();

// Test DB connection at startup
var dbLogger = app.Services.GetRequiredService<ActivityLogger>();
await dbLogger.TestConnectionAsync();

app.UseCors();

// Security response headers
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    ctx.Response.Headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

Console.WriteLine($"GitXO backend running at http://localhost:{appPort}");
Console.WriteLine($"Public repositories:  {reposDir}");
Console.WriteLine($"Private repositories: {privateReposDir}");

app.Run();
