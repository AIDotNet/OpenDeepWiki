using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Auth;

namespace OpenDeepWiki.MCP;

/// <summary>
/// OAuth 2.1 Authorization Server that wraps Google OAuth for MCP client authentication.
/// Claude.ai and other MCP clients expect the MCP server to act as its own OAuth AS.
/// This implementation proxies the OAuth flow through Google and issues internal JWTs.
/// </summary>
public class McpOAuthServer
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<McpOAuthServer> _logger;
    private readonly string _googleClientId;
    private readonly string _googleClientSecret;

    // In-memory stores for OAuth state (short-lived, lost on restart = clients re-auth)
    private static readonly ConcurrentDictionary<string, PendingAuthorization> PendingAuthorizations = new();
    private static readonly ConcurrentDictionary<string, AuthorizationCode> AuthorizationCodes = new();

    // Allowed redirect URIs for MCP clients
    private static readonly HashSet<string> AllowedRedirectUris = new(StringComparer.OrdinalIgnoreCase)
    {
        "https://claude.ai/api/mcp/auth_callback",
        "https://claude.com/api/mcp/auth_callback",
        "https://www.claude.ai/api/mcp/auth_callback",
    };

    public McpOAuthServer(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<McpOAuthServer> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;

        _googleClientId = configuration["GOOGLE_CLIENT_ID"]
                          ?? configuration["Google:ClientId"]
                          ?? throw new InvalidOperationException(
                              "GOOGLE_CLIENT_ID is required for MCP OAuth");

        _googleClientSecret = configuration["GOOGLE_CLIENT_SECRET"]
                              ?? configuration["Google:ClientSecret"]
                              ?? throw new InvalidOperationException(
                                  "GOOGLE_CLIENT_SECRET is required for MCP OAuth authorization server");
    }

    /// <summary>
    /// Returns OAuth Authorization Server Metadata (RFC 8414).
    /// </summary>
    public IResult HandleAuthorizationServerMetadata(HttpContext context)
    {
        var baseUrl = GetBaseUrl(context);

        var metadata = new
        {
            issuer = baseUrl,
            authorization_endpoint = $"{baseUrl}/oauth/authorize",
            token_endpoint = $"{baseUrl}/oauth/token",
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code" },
            code_challenge_methods_supported = new[] { "S256" },
            scopes_supported = new[] { "openid", "email", "profile" },
            token_endpoint_auth_methods_supported = new[] { "none" },
        };

        return Results.Json(metadata);
    }

    /// <summary>
    /// Handles the /oauth/authorize request from MCP clients.
    /// Stores the pending authorization and redirects to Google OAuth.
    /// </summary>
    public IResult HandleAuthorize(HttpContext context)
    {
        var query = context.Request.Query;

        var clientId = query["client_id"].FirstOrDefault();
        var redirectUri = query["redirect_uri"].FirstOrDefault();
        var codeChallenge = query["code_challenge"].FirstOrDefault();
        var codeChallengeMethod = query["code_challenge_method"].FirstOrDefault();
        var state = query["state"].FirstOrDefault();
        var scope = query["scope"].FirstOrDefault();

        // Validate required parameters
        if (string.IsNullOrEmpty(redirectUri))
        {
            _logger.LogWarning("MCP OAuth authorize: missing redirect_uri");
            return Results.BadRequest(new { error = "invalid_request", error_description = "redirect_uri is required" });
        }

        if (!AllowedRedirectUris.Contains(redirectUri))
        {
            _logger.LogWarning("MCP OAuth authorize: disallowed redirect_uri: {RedirectUri}", redirectUri);
            return Results.BadRequest(new { error = "invalid_request", error_description = "redirect_uri not allowed" });
        }

        if (string.IsNullOrEmpty(codeChallenge) || codeChallengeMethod != "S256")
        {
            _logger.LogWarning("MCP OAuth authorize: missing or invalid PKCE parameters");
            return Results.BadRequest(new { error = "invalid_request", error_description = "PKCE with S256 is required" });
        }

        // Generate internal state to map our Google callback back to this request
        var internalState = GenerateRandomString(32);
        var baseUrl = GetBaseUrl(context);

        var pending = new PendingAuthorization
        {
            ClientId = clientId ?? string.Empty,
            RedirectUri = redirectUri,
            CodeChallenge = codeChallenge,
            State = state ?? string.Empty,
            Scope = scope ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
        };

        PendingAuthorizations[internalState] = pending;

        _logger.LogInformation("MCP OAuth authorize: redirecting to Google OAuth (client_id: {ClientId})", clientId);

        // Redirect to Google OAuth
        var googleAuthUrl = "https://accounts.google.com/o/oauth2/v2/auth"
                            + $"?client_id={Uri.EscapeDataString(_googleClientId)}"
                            + $"&redirect_uri={Uri.EscapeDataString($"{baseUrl}/oauth/callback")}"
                            + "&response_type=code"
                            + "&scope=openid%20email%20profile"
                            + $"&state={Uri.EscapeDataString(internalState)}"
                            + "&access_type=offline"
                            + "&prompt=consent";

        return Results.Redirect(googleAuthUrl);
    }

    /// <summary>
    /// Handles the Google OAuth callback.
    /// Exchanges the Google code for tokens, resolves the user, and redirects back to the MCP client.
    /// </summary>
    public async Task<IResult> HandleCallback(HttpContext context)
    {
        var query = context.Request.Query;
        var googleCode = query["code"].FirstOrDefault();
        var internalState = query["state"].FirstOrDefault();
        var error = query["error"].FirstOrDefault();

        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("MCP OAuth callback: Google returned error: {Error}", error);
            return Results.BadRequest(new { error = "access_denied", error_description = $"Google OAuth error: {error}" });
        }

        if (string.IsNullOrEmpty(googleCode) || string.IsNullOrEmpty(internalState))
        {
            _logger.LogWarning("MCP OAuth callback: missing code or state");
            return Results.BadRequest(new { error = "invalid_request", error_description = "Missing code or state" });
        }

        // Look up the pending authorization
        if (!PendingAuthorizations.TryRemove(internalState, out var pending))
        {
            _logger.LogWarning("MCP OAuth callback: unknown or expired state");
            return Results.BadRequest(new { error = "invalid_request", error_description = "Unknown or expired state" });
        }

        if (pending.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("MCP OAuth callback: expired authorization request");
            return Results.BadRequest(new { error = "invalid_request", error_description = "Authorization request expired" });
        }

        var baseUrl = GetBaseUrl(context);

        // Exchange Google code for tokens
        var client = _httpClientFactory.CreateClient();
        var tokenRequest = new Dictionary<string, string>
        {
            ["code"] = googleCode,
            ["client_id"] = _googleClientId,
            ["client_secret"] = _googleClientSecret,
            ["redirect_uri"] = $"{baseUrl}/oauth/callback",
            ["grant_type"] = "authorization_code",
        };

        var tokenResponse = await client.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(tokenRequest));

        if (!tokenResponse.IsSuccessStatusCode)
        {
            var errorBody = await tokenResponse.Content.ReadAsStringAsync();
            _logger.LogError("MCP OAuth callback: Google token exchange failed: {Status} {Body}",
                tokenResponse.StatusCode, errorBody);
            return Results.BadRequest(new { error = "server_error", error_description = "Failed to exchange code with Google" });
        }

        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
        var tokenData = JsonSerializer.Deserialize<JsonElement>(tokenJson);

        // Extract user info from the ID token
        var idToken = tokenData.GetProperty("id_token").GetString();
        if (string.IsNullOrEmpty(idToken))
        {
            _logger.LogError("MCP OAuth callback: no id_token in Google response");
            return Results.BadRequest(new { error = "server_error", error_description = "No ID token from Google" });
        }

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(idToken);
        var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
        var name = jwt.Claims.FirstOrDefault(c => c.Type == "name")?.Value ?? email;

        if (string.IsNullOrEmpty(email))
        {
            _logger.LogError("MCP OAuth callback: no email in Google ID token");
            return Results.BadRequest(new { error = "server_error", error_description = "No email in Google token" });
        }

        // Look up or create user in database
        string userId;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IContext>();

            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted);

            if (user == null)
            {
                // Create new user
                user = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name ?? email,
                    Email = email,
                    Status = 1,
                    CreatedAt = DateTime.UtcNow,
                };
                dbContext.Users.Add(user);

                // Assign default role
                var defaultRole = await dbContext.Roles
                    .FirstOrDefaultAsync(r => r.Name == "User" && !r.IsDeleted);

                if (defaultRole != null)
                {
                    dbContext.UserRoles.Add(new UserRole
                    {
                        Id = Guid.NewGuid().ToString(),
                        UserId = user.Id,
                        RoleId = defaultRole.Id,
                        CreatedAt = DateTime.UtcNow,
                    });
                }

                _logger.LogInformation("MCP OAuth: created new user {Email}", email);
            }

            user.LastLoginAt = DateTime.UtcNow;
            user.UpdateTimestamp();
            await dbContext.SaveChangesAsync();
            userId = user.Id;
        }

        // Generate our authorization code
        var authCode = GenerateRandomString(48);
        AuthorizationCodes[authCode] = new AuthorizationCode
        {
            Code = authCode,
            UserId = userId,
            Email = email,
            Name = name ?? email,
            ClientId = pending.ClientId,
            RedirectUri = pending.RedirectUri,
            CodeChallenge = pending.CodeChallenge,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
        };

        _logger.LogInformation("MCP OAuth callback: issuing auth code for {Email}, redirecting to {RedirectUri}", email, pending.RedirectUri);

        // Redirect back to the MCP client with our authorization code
        var redirectUrl = $"{pending.RedirectUri}?code={Uri.EscapeDataString(authCode)}";
        if (!string.IsNullOrEmpty(pending.State))
        {
            redirectUrl += $"&state={Uri.EscapeDataString(pending.State)}";
        }

        // Return HTML page that redirects and then closes the tab
        var safeUrl = System.Text.Json.JsonSerializer.Serialize(redirectUrl);
        var html = "<!DOCTYPE html><html><head><title>Authorizing...</title></head><body>"
                   + "<p>Authentication successful. Redirecting...</p>"
                   + "<script>window.location.href=" + safeUrl + ";"
                   + "setTimeout(function(){window.close();},2000);</script>"
                   + "</body></html>";
        return Results.Content(html, "text/html");
    }

    /// <summary>
    /// Handles the POST /oauth/token request from MCP clients.
    /// Validates PKCE and exchanges our authorization code for a JWT.
    /// </summary>
    public async Task<IResult> HandleToken(HttpContext context)
    {
        // Read form data
        var form = await context.Request.ReadFormAsync();
        var grantType = form["grant_type"].FirstOrDefault();
        var code = form["code"].FirstOrDefault();
        var codeVerifier = form["code_verifier"].FirstOrDefault();
        var clientId = form["client_id"].FirstOrDefault();
        var redirectUri = form["redirect_uri"].FirstOrDefault();

        if (grantType != "authorization_code")
        {
            return Results.Json(
                new { error = "unsupported_grant_type", error_description = "Only authorization_code is supported" },
                statusCode: 400);
        }

        if (string.IsNullOrEmpty(code))
        {
            return Results.Json(
                new { error = "invalid_request", error_description = "code is required" },
                statusCode: 400);
        }

        // Look up and consume the authorization code (single-use)
        if (!AuthorizationCodes.TryRemove(code, out var authCode))
        {
            _logger.LogWarning("MCP OAuth token: unknown or already-used code");
            return Results.Json(
                new { error = "invalid_grant", error_description = "Invalid or expired authorization code" },
                statusCode: 400);
        }

        if (authCode.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("MCP OAuth token: expired authorization code");
            return Results.Json(
                new { error = "invalid_grant", error_description = "Authorization code expired" },
                statusCode: 400);
        }

        // Validate client_id and redirect_uri match
        if (!string.IsNullOrEmpty(clientId) && clientId != authCode.ClientId)
        {
            _logger.LogWarning("MCP OAuth token: client_id mismatch");
            return Results.Json(
                new { error = "invalid_grant", error_description = "client_id mismatch" },
                statusCode: 400);
        }

        if (!string.IsNullOrEmpty(redirectUri) && redirectUri != authCode.RedirectUri)
        {
            _logger.LogWarning("MCP OAuth token: redirect_uri mismatch");
            return Results.Json(
                new { error = "invalid_grant", error_description = "redirect_uri mismatch" },
                statusCode: 400);
        }

        // Validate PKCE
        if (!string.IsNullOrEmpty(authCode.CodeChallenge))
        {
            if (string.IsNullOrEmpty(codeVerifier))
            {
                _logger.LogWarning("MCP OAuth token: missing code_verifier");
                return Results.Json(
                    new { error = "invalid_grant", error_description = "code_verifier is required" },
                    statusCode: 400);
            }

            var expectedChallenge = ComputeS256Challenge(codeVerifier);
            if (expectedChallenge != authCode.CodeChallenge)
            {
                _logger.LogWarning("MCP OAuth token: PKCE verification failed");
                return Results.Json(
                    new { error = "invalid_grant", error_description = "PKCE verification failed" },
                    statusCode: 400);
            }
        }

        // Generate JWT using existing JwtService
        string accessToken;
        using (var scope = _scopeFactory.CreateScope())
        {
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<IContext>();

            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == authCode.UserId && !u.IsDeleted);
            if (user == null)
            {
                return Results.Json(
                    new { error = "invalid_grant", error_description = "User not found" },
                    statusCode: 400);
            }

            var roles = await dbContext.UserRoles
                .Where(ur => ur.UserId == user.Id && !ur.IsDeleted)
                .Join(dbContext.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
                .ToListAsync();

            accessToken = jwtService.GenerateToken(user, roles);
        }

        _logger.LogInformation("MCP OAuth token: issued JWT for {Email}", authCode.Email);

        return Results.Json(new
        {
            access_token = accessToken,
            token_type = "Bearer",
            expires_in = 86400, // 24 hours (matches JwtOptions default)
        });
    }

    /// <summary>
    /// Removes expired entries from the in-memory stores.
    /// </summary>
    public static void CleanupExpiredEntries()
    {
        var now = DateTime.UtcNow;

        foreach (var kvp in PendingAuthorizations)
        {
            if (kvp.Value.ExpiresAt < now)
                PendingAuthorizations.TryRemove(kvp.Key, out _);
        }

        foreach (var kvp in AuthorizationCodes)
        {
            if (kvp.Value.ExpiresAt < now)
                AuthorizationCodes.TryRemove(kvp.Key, out _);
        }
    }

    private string GetBaseUrl(HttpContext context)
    {
        var baseUrl = _configuration["ASPNETCORE_CORS_ORIGINS"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            var request = context.Request;
            var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
            var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.ToString();
            baseUrl = $"{scheme}://{host}";
        }

        return baseUrl;
    }

    private static string GenerateRandomString(int length)
    {
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=')[..length];
    }

    private static string ComputeS256Challenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}

/// <summary>
/// Extension methods to map MCP OAuth endpoints in Program.cs.
/// </summary>
public static class McpOAuthEndpoints
{
    public static WebApplication MapMcpOAuthEndpoints(this WebApplication app)
    {
        var oauthServer = app.Services.GetRequiredService<McpOAuthServer>();

        // Authorization server metadata (RFC 8414)
        app.MapGet("/.well-known/oauth-authorization-server",
                oauthServer.HandleAuthorizationServerMetadata)
            .AllowAnonymous();
        app.MapGet("/api/.well-known/oauth-authorization-server",
                oauthServer.HandleAuthorizationServerMetadata)
            .AllowAnonymous();

        // OAuth endpoints (proxied from frontend via /oauth/* catch-all route)
        // NOTE: Handlers returning async Task<IResult> with HttpContext parameter are
        // ambiguous with RequestDelegate (HttpContext -> Task) due to covariance.
        // We must explicitly execute the IResult to avoid ASP.NET ignoring the return value.
        app.MapGet("/oauth/authorize", oauthServer.HandleAuthorize)
            .AllowAnonymous();
        app.MapGet("/oauth/callback", async (HttpContext ctx) =>
            {
                var result = await oauthServer.HandleCallback(ctx);
                await result.ExecuteAsync(ctx);
            })
            .AllowAnonymous();
        app.MapPost("/oauth/token", async (HttpContext ctx) =>
            {
                var result = await oauthServer.HandleToken(ctx);
                await result.ExecuteAsync(ctx);
            })
            .AllowAnonymous();

        return app;
    }
}

/// <summary>
/// Background service that periodically cleans up expired OAuth state.
/// </summary>
public class McpOAuthCleanupService : BackgroundService
{
    private readonly ILogger<McpOAuthCleanupService> _logger;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);

    public McpOAuthCleanupService(ILogger<McpOAuthCleanupService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(CleanupInterval, stoppingToken);
            McpOAuthServer.CleanupExpiredEntries();
            _logger.LogDebug("MCP OAuth: cleaned up expired entries");
        }
    }
}

// Data structures for in-memory OAuth state

internal class PendingAuthorization
{
    public required string ClientId { get; init; }
    public required string RedirectUri { get; init; }
    public required string CodeChallenge { get; init; }
    public required string State { get; init; }
    public required string Scope { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime ExpiresAt { get; init; }
}

internal class AuthorizationCode
{
    public required string Code { get; init; }
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public required string Name { get; init; }
    public required string ClientId { get; init; }
    public required string RedirectUri { get; init; }
    public required string CodeChallenge { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime ExpiresAt { get; init; }
}
