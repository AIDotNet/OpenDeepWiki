using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace OpenDeepWiki.MCP;

/// <summary>
/// Configures MCP server authentication using Google as the OAuth 2.1 authorization server.
/// Implements Protected Resource Metadata (RFC 9728) for MCP client discovery.
/// </summary>
public static class McpAuthConfiguration
{
    public const string McpGoogleScheme = "McpGoogle";
    public const string McpPolicyName = "McpAccess";

    /// <summary>
    /// Adds Google OAuth token validation as a secondary authentication scheme for MCP.
    /// </summary>
    public static AuthenticationBuilder AddMcpGoogleAuth(
        this AuthenticationBuilder builder,
        IConfiguration configuration)
    {
        var googleClientId = configuration["GOOGLE_CLIENT_ID"]
                             ?? configuration["Google:ClientId"]
                             ?? throw new InvalidOperationException(
                                 "GOOGLE_CLIENT_ID is required for MCP OAuth authentication");

        builder.AddJwtBearer(McpGoogleScheme, options =>
        {
            options.Authority = "https://accounts.google.com";
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = new[]
                {
                    "https://accounts.google.com",
                    "accounts.google.com"
                },
                ValidateAudience = true,
                ValidAudience = googleClientId,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                NameClaimType = "name",
                RoleClaimType = "roles"
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("OpenDeepWiki.MCP.McpAuthConfiguration");
                    logger.LogDebug("MCP Google auth failed: {Error}", context.Exception.Message);
                    return Task.CompletedTask;
                }
            };
        });

        return builder;
    }

    /// <summary>
    /// Maps the Protected Resource Metadata endpoint (RFC 9728).
    /// Claude and other MCP clients fetch this to discover the authorization server.
    /// Mapped at both /.well-known/ (standard) and /api/.well-known/ (for reverse proxy setups).
    /// </summary>
    public static WebApplication MapProtectedResourceMetadata(
        this WebApplication app)
    {
        app.MapGet("/.well-known/oauth-protected-resource", HandleProtectedResourceMetadata)
            .AllowAnonymous();
        app.MapGet("/api/.well-known/oauth-protected-resource", HandleProtectedResourceMetadata)
            .AllowAnonymous();

        return app;
    }

    private static IResult HandleProtectedResourceMetadata(HttpContext context)
    {
        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        // Derive public base URL from CORS origins or forwarded headers
        var baseUrl = config["ASPNETCORE_CORS_ORIGINS"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            var request = context.Request;
            var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
            var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.ToString();
            baseUrl = $"{scheme}://{host}";
        }

        var metadata = new
        {
            resource = $"{baseUrl}/api/mcp",
            authorization_servers = new[] { baseUrl },
            scopes_supported = new[] { "openid", "email", "profile" },
            resource_documentation = $"{baseUrl}/docs/mcp"
        };

        return Results.Json(metadata);
    }
}
