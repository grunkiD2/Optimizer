using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Optimizer.Server.Auth;
using Optimizer.Server.Data;
using Optimizer.Server.Endpoints;
using Optimizer.Server.Models;
using Optimizer.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=optimizer-server.db";
builder.Services.AddDbContext<OptimizerDbContext>(opt => opt.UseSqlite(connectionString));

// Services
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddScoped<IMarketplaceService, MarketplaceService>();
builder.Services.AddScoped<IPluginMarketplaceService, PluginMarketplaceService>();
builder.Services.AddSingleton<IPluginSigningService, PluginSigningService>();
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
builder.Services.AddScoped<IWebhookService, WebhookService>();
builder.Services.AddScoped<IFederatedLearningService, FederatedLearningService>();
builder.Services.AddHttpClient("webhook");

// Email: console for dev, smtp if Smtp:Host configured
if (!string.IsNullOrEmpty(builder.Configuration["Smtp:Host"]))
    builder.Services.AddSingleton<IEmailService, SmtpEmailService>();
else
    builder.Services.AddSingleton<IEmailService, ConsoleEmailService>();

// JWT config values
const string DevJwtFallbackSecret = "DEV_ONLY_NEVER_USE_IN_PROD_64+_CHAR_SECRET_KEY_REPLACE_ME_PLEASE";
var configuredJwtSecret = builder.Configuration["Jwt:Secret"];
var jwtSecret   = configuredJwtSecret ?? DevJwtFallbackSecret;
var jwtIssuer   = builder.Configuration["Jwt:Issuer"]   ?? "optimizer-server";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "optimizer-client";

// Guard: refuse the predictable dev fallback secret outside Development.
// A committed/known signing secret means anyone can forge JWTs.
if (!builder.Environment.IsDevelopment() &&
    (string.IsNullOrEmpty(configuredJwtSecret) || configuredJwtSecret == DevJwtFallbackSecret))
{
    throw new InvalidOperationException(
        "Jwt:Secret must be configured with a strong, unique value outside the Development environment " +
        "(set it via environment variable or user-secrets). Refusing to start with the dev fallback secret.");
}

// Auth: policy scheme that picks JWT vs ApiKey based on headers
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme          = "JWT_OR_APIKEY";
    options.DefaultChallengeScheme = "JWT_OR_APIKEY";
})
.AddPolicyScheme("JWT_OR_APIKEY", "JWT or API Key", options =>
{
    options.ForwardDefaultSelector = ctx =>
    {
        if (ctx.Request.Headers.ContainsKey("X-Api-Key"))
            return "ApiKey";
        var auth = ctx.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
            return "ApiKey";
        return JwtBearerDefaults.AuthenticationScheme;
    };
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidIssuer              = jwtIssuer,
        ValidateAudience         = true,
        ValidAudience            = jwtAudience,
        ValidateLifetime         = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew                = TimeSpan.FromSeconds(30)
    };

    // Add auth_method=jwt claim so scope policies can distinguish JWT from API-key users
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = ctx =>
        {
            var identity = (System.Security.Claims.ClaimsIdentity?)ctx.Principal?.Identity;
            identity?.AddClaim(new System.Security.Claims.Claim("auth_method", "jwt"));
            return Task.CompletedTask;
        }
    };
})
.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", null);

// Authorization: default policy + per-scope policies
builder.Services.AddAuthorization(options =>
{
    // Default policy: just require any authenticated user
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes("JWT_OR_APIKEY", JwtBearerDefaults.AuthenticationScheme, "ApiKey")
        .RequireAuthenticatedUser()
        .Build();

    // Named policy to restrict endpoints to JWT-only (for key management)
    options.AddPolicy(JwtBearerDefaults.AuthenticationScheme,
        policy => policy
            .AddAuthenticationSchemes("JWT_OR_APIKEY", JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim("auth_method", "jwt"));

    // Scope policies: JWT users (interactive) implicitly pass all — they have auth_method=jwt.
    // API keys are limited to their granted scopes.
    foreach (var scope in ApiScopes.All)
    {
        options.AddPolicy($"scope:{scope}", policy =>
            policy
                .AddAuthenticationSchemes("JWT_OR_APIKEY", JwtBearerDefaults.AuthenticationScheme, "ApiKey")
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx =>
                    ctx.User.HasClaim("auth_method", "jwt") ||
                    ctx.User.HasClaim("scope", scope)));
    }
});

// Rate limiting
var permitPerMinute = builder.Configuration.GetValue<int>("RateLimit:PermitPerMinute", 100);
var authPermitPerMinute = builder.Configuration.GetValue<int>("RateLimit:AuthPermitPerMinute", 10);

builder.Services.AddRateLimiter(rl =>
{
    rl.RejectionStatusCode = 429;
    rl.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            """{"code":"rate_limit_exceeded","message":"Too many requests. Please slow down."}""", ct);
    };

    // Global limiter: applies to ALL requests unless overridden by a named policy.
    // Keyed by API key header > JWT sub > IP.
    rl.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpCtx =>
    {
        string partitionKey;
        if (httpCtx.Request.Headers.TryGetValue("X-Api-Key", out var keyHeader))
        {
            partitionKey = "apikey:" + keyHeader.ToString();
        }
        else
        {
            var sub = httpCtx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                   ?? httpCtx.User.FindFirst("sub")?.Value;
            partitionKey = sub != null
                ? "user:" + sub
                : "ip:" + (httpCtx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit          = permitPerMinute,
                Window               = TimeSpan.FromMinutes(1),
                SegmentsPerWindow    = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            });
    });

    // Named policy for auth endpoints — stricter to resist magic-link abuse.
    // Applied per-IP, not per-user, since auth requests are unauthenticated.
    rl.AddPolicy("auth", httpCtx =>
    {
        var ip = httpCtx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter("auth:" + ip, _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit          = authPermitPerMinute,
                Window               = TimeSpan.FromMinutes(1),
                SegmentsPerWindow    = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            });
    });
});

// OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// CORS for browser clients (PWA)
builder.Services.AddCors(opts => opts.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// Ensure DB created and seed marketplace
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OptimizerDbContext>();
    db.Database.EnsureCreated();
    var seederLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await MarketplaceSeeder.SeedAsync(db, seederLogger);
    var pluginSigning = scope.ServiceProvider.GetRequiredService<IPluginSigningService>();
    await PluginSeeder.SeedAsync(db, pluginSigning, seederLogger);
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapHealth();
app.MapAuth(authPermitPerMinute);
app.MapSync();
app.MapMarketplace();
app.MapPlugins();
app.MapApiKeys();
app.MapWebhooks();
app.MapFederated();

// Protected example endpoint to verify JWT works
app.MapGet("/api/me", (HttpContext ctx) =>
{
    var sub  = ctx.User.FindFirst("sub")?.Value;
    var email = ctx.User.FindFirst("email")?.Value;
    return Results.Ok(new { id = sub, email });
}).RequireAuthorization().WithTags("Account");

app.Run();

// Make Program accessible to tests via WebApplicationFactory<Program>
public partial class Program { }
