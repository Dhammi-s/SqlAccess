using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SqlAccess.Api.Vault.Services;
using SqlAccess.Api.Cicd.Hubs;
using SqlAccess.Api.Cicd.Providers;
using SqlAccess.Api.Cicd.Services;
using SqlAccess.Api.Data;
using SqlAccess.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration objects ----------
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrWhiteSpace(jwtSettings.Key) || jwtSettings.Key.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key must be configured and at least 32 characters. Set it via user-secrets or an env var.");
builder.Services.AddSingleton(jwtSettings);

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                  ?? new[] { "http://localhost:5173", "http://localhost:5174", "https://sql-access.vercel.app" };

// ---------- Services ----------
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("MasterDb")
                     ?? throw new InvalidOperationException("ConnectionStrings:MasterDb is not configured.")));

builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IAgencyService, AgencyService>();
builder.Services.AddScoped<IDeploymentService, DeploymentService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ISourceBuildService, SourceBuildService>();

// ---------- CI/CD deployment portal ----------
builder.Services.AddSignalR();
builder.Services.AddSingleton<IGitService, GitService>();
builder.Services.AddSingleton<IBuildService, BuildService>();
builder.Services.AddSingleton<IGitHubService, GitHubService>();
builder.Services.AddSingleton<IGitHubActionsService, GitHubActionsService>();
builder.Services.AddSingleton<IDeploymentProvider, FtpDeploymentProvider>();
builder.Services.AddSingleton<IDeploymentProvider, SftpDeploymentProvider>();
builder.Services.AddSingleton<ILogService, LogService>();
builder.Services.AddSingleton<IDeploymentQueue, DeploymentQueue>();
builder.Services.AddScoped<IDeploymentOrchestrator, DeploymentOrchestrator>();
builder.Services.AddScoped<IWebsiteService, WebsiteService>();
builder.Services.AddScoped<ICicdDeploymentService, CicdDeploymentService>();
builder.Services.AddHostedService<DeploymentBackgroundService>();

// ---------- Secret Vault ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IVaultAuditService, VaultAuditService>();
builder.Services.AddScoped<IVaultService, VaultService>();

// ---------- In-memory cache store (Redis-like) ----------
builder.Services.Configure<SqlAccess.Api.Cache.Models.CacheOptions>(
    builder.Configuration.GetSection(SqlAccess.Api.Cache.Models.CacheOptions.SectionName));

// Persistence sink: file-based (AOF + snapshot) unless PersistenceMode = None.
var persistenceMode = builder.Configuration[$"{SqlAccess.Api.Cache.Models.CacheOptions.SectionName}:PersistenceMode"] ?? "Both";
if (persistenceMode.Equals("None", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<SqlAccess.Api.Cache.Interfaces.ICachePersistence, SqlAccess.Api.Cache.Persistence.NullPersistence>();
else
    builder.Services.AddSingleton<SqlAccess.Api.Cache.Interfaces.ICachePersistence, SqlAccess.Api.Cache.Persistence.FilePersistence>();

builder.Services.AddSingleton<SqlAccess.Api.Cache.Interfaces.ICacheStore, SqlAccess.Api.Cache.Services.InMemoryCacheStore>();
builder.Services.AddHostedService<SqlAccess.Api.Cache.Workers.CacheCleanupWorker>();
builder.Services.AddHostedService<SqlAccess.Api.Cache.Workers.PersistenceWorker>();
builder.Services.AddHostedService<SqlAccess.Api.Cache.Workers.MetricsBroadcastWorker>();
// TCP/RESP server
builder.Services.AddSingleton<SqlAccess.Api.Cache.Networking.IConnectionManager, SqlAccess.Api.Cache.Networking.ConnectionManager>();
builder.Services.AddSingleton<SqlAccess.Api.Cache.Monitoring.IMonitoringService, SqlAccess.Api.Cache.Monitoring.MonitoringService>();
builder.Services.AddSingleton<SqlAccess.Api.Cache.Networking.CommandExecutor>();
builder.Services.AddHostedService<SqlAccess.Api.Cache.Networking.CacheTcpServer>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("vault-auth", o => { o.Window = TimeSpan.FromMinutes(1); o.PermitLimit = 10; o.QueueLimit = 0; });
    options.AddFixedWindowLimiter("vault-read", o => { o.Window = TimeSpan.FromMinutes(1); o.PermitLimit = 60; o.QueueLimit = 0; });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // SignalR sends the JWT via query string on the WebSocket handshake.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(options =>
{
    // Application token (has vault_app_id) vs the human admin (any other valid JWT).
    options.AddPolicy("VaultApp", p => p.RequireClaim("vault_app_id"));
    options.AddPolicy("VaultAdmin", p => p.RequireAssertion(ctx =>
        ctx.User.Identity?.IsAuthenticated == true && !ctx.User.HasClaim(c => c.Type == "vault_app_id")));
});

builder.Services.AddCors(options =>
    options.AddPolicy("spa", p => p
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // required for SignalR websockets from the SPA origin

var app = builder.Build();

// Fail fast if the encryption key is misconfigured (constructs the service once).
_ = app.Services.GetRequiredService<IEncryptionService>();

// Recover the cache from disk BEFORE serving traffic: load the snapshot, then replay the AOF.
app.Services.GetRequiredService<SqlAccess.Api.Cache.Interfaces.ICachePersistence>()
    .Recover(app.Services.GetRequiredService<SqlAccess.Api.Cache.Interfaces.ICacheStore>());

// Global exception handling — returns clean ProblemDetails, never a stack trace to clients.
app.UseExceptionHandler(errApp => errApp.Run(async context =>
{
    var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var ex = feature?.Error;
    var log = context.RequestServices.GetRequiredService<ILogger<Program>>();
    log.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);

    var isDbError = ex is Microsoft.Data.SqlClient.SqlException or Microsoft.EntityFrameworkCore.DbUpdateException;
    context.Response.StatusCode = isDbError ? StatusCodes.Status502BadGateway : StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new
    {
        message = isDbError
            ? "Database error. Check the agency's connection details and that the server is reachable."
            : "An unexpected error occurred.",
    });
}));

app.UseHttpsRedirection();
app.UseCors("spa");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<DeploymentHub>("/hubs/deployment");
app.MapHub<SqlAccess.Api.Cache.Hubs.CacheMetricsHub>("/hubs/cache");

app.Run();
