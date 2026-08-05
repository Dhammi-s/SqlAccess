using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
builder.Services.AddSingleton<IDeploymentProvider, FtpDeploymentProvider>();
builder.Services.AddSingleton<ILogService, LogService>();
builder.Services.AddSingleton<IDeploymentQueue, DeploymentQueue>();
builder.Services.AddScoped<IDeploymentOrchestrator, DeploymentOrchestrator>();
builder.Services.AddScoped<IWebsiteService, WebsiteService>();
builder.Services.AddScoped<ICicdDeploymentService, CicdDeploymentService>();
builder.Services.AddHostedService<DeploymentBackgroundService>();

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
builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
    options.AddPolicy("spa", p => p
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // required for SignalR websockets from the SPA origin

var app = builder.Build();

// Fail fast if the encryption key is misconfigured (constructs the service once).
_ = app.Services.GetRequiredService<IEncryptionService>();

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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<DeploymentHub>("/hubs/deployment");

app.Run();
