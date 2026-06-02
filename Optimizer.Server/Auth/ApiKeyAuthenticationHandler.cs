using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Optimizer.Server.Services;

namespace Optimizer.Server.Auth;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IApiKeyService _apiKeyService;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyService apiKeyService)
        : base(options, logger, encoder)
    {
        _apiKeyService = apiKeyService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Try X-Api-Key header first
        string? rawKey = null;

        if (Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            rawKey = apiKeyHeader.ToString();
        }
        else
        {
            var authHeader = Request.Headers.Authorization.ToString();
            if (authHeader.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
                rawKey = authHeader["ApiKey ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(rawKey))
            return AuthenticateResult.NoResult();

        var result = await _apiKeyService.ValidateAsync(rawKey);
        if (result == null)
            return AuthenticateResult.Fail("Invalid, revoked, or expired API key.");

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, result.UserId.ToString()),
            new Claim("auth_method", "apikey")
        };

        foreach (var scope in result.Scopes)
            claims.Add(new Claim("scope", scope));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
