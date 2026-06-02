using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Optimizer.Server.Data;
using Optimizer.Server.Endpoints;
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

// Email: console for dev, smtp if Smtp:Host configured
if (!string.IsNullOrEmpty(builder.Configuration["Smtp:Host"]))
    builder.Services.AddSingleton<IEmailService, SmtpEmailService>();
else
    builder.Services.AddSingleton<IEmailService, ConsoleEmailService>();

// JWT Auth
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "DEV_ONLY_NEVER_USE_IN_PROD_64+_CHAR_SECRET_KEY_REPLACE_ME_PLEASE";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "optimizer-server";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "optimizer-client";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

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
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapHealth();
app.MapAuth();
app.MapSync();
app.MapMarketplace();

// Protected example endpoint to verify JWT works
app.MapGet("/api/me", (HttpContext ctx) =>
{
    var sub = ctx.User.FindFirst("sub")?.Value;
    var email = ctx.User.FindFirst("email")?.Value;
    return Results.Ok(new { id = sub, email });
}).RequireAuthorization().WithTags("Account").WithOpenApi();

app.Run();

// Make Program accessible to tests via WebApplicationFactory<Program>
public partial class Program { }
