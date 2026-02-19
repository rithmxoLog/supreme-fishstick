using System.Text;
using GitXO.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:3001");

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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddSingleton<ActivityLogger>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<RepoDbService>();

// Ensure repositories directory exists
var reposDir = builder.Configuration["ReposDirectory"] ?? "repositories";
if (!Path.IsPathRooted(reposDir))
    reposDir = Path.GetFullPath(reposDir, Directory.GetCurrentDirectory());

if (!Directory.Exists(reposDir))
    Directory.CreateDirectory(reposDir);

// Store resolved path back for controllers to use
builder.Configuration["ReposDirectory"] = reposDir;

var app = builder.Build();

// Test DB connection at startup
var dbLogger = app.Services.GetRequiredService<ActivityLogger>();
await dbLogger.TestConnectionAsync();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

Console.WriteLine($"GitXO backend running at http://localhost:3001");
Console.WriteLine($"Repositories stored at: {reposDir}");

app.Run();
