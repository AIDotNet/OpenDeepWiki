using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.GitHub;

/// <summary>
/// Implements GitHub App authentication and API operations.
/// Uses JWT signed with the app's private key to generate installation tokens.
/// </summary>
public class GitHubAppService : IGitHubAppService
{
    private readonly IConfiguration _configuration;
    private readonly GitHubAppCredentialCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IContext _context;
    private readonly ILogger<GitHubAppService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public GitHubAppService(
        IConfiguration configuration,
        GitHubAppCredentialCache cache,
        IHttpClientFactory httpClientFactory,
        IContext context,
        ILogger<GitHubAppService> logger)
    {
        _configuration = configuration;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _context = context;
        _logger = logger;
    }

    private string? AppId =>
        _cache.AppId
        ?? _configuration["GitHub:App:Id"]
        ?? Environment.GetEnvironmentVariable("GitHub__App__Id")
        ?? Environment.GetEnvironmentVariable("GITHUB_APP_ID");

    private string? PrivateKeyBase64 =>
        _cache.PrivateKeyBase64
        ?? _configuration["GitHub:App:PrivateKey"]
        ?? Environment.GetEnvironmentVariable("GitHub__App__PrivateKey")
        ?? Environment.GetEnvironmentVariable("GITHUB_APP_PRIVATE_KEY");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(PrivateKeyBase64);

    /// <summary>
    /// Generate a JWT for authenticating as the GitHub App.
    /// JWT is valid for 10 minutes (GitHub maximum).
    /// </summary>
    private string GenerateJwt()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("GitHub App is not configured. Set GitHub:App:Id and GitHub:App:PrivateKey.");

        var pemBytes = Convert.FromBase64String(PrivateKeyBase64!);
        var pemContent = System.Text.Encoding.UTF8.GetString(pemBytes);

        var rsa = RSA.Create();
        rsa.ImportFromPem(pemContent.AsSpan());

        var securityKey = new RsaSecurityKey(rsa);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: AppId,
            claims: new[]
            {
                new Claim("iat", new DateTimeOffset(now.AddSeconds(-60)).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            },
            expires: now.AddMinutes(10),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("GitHubApp");
        client.BaseAddress = new Uri("https://api.github.com");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenDeepWiki", "1.0"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    public async Task<List<GitHubInstallationInfo>> ListInstallationsAsync(CancellationToken cancellationToken = default)
    {
        var jwt = GenerateJwt();
        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await client.GetAsync("/app/installations", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var installations = JsonSerializer.Deserialize<List<GitHubInstallationResponse>>(json, JsonOptions) ?? new();

        return installations.Select(i => new GitHubInstallationInfo
        {
            Id = i.Id,
            AccountLogin = i.Account?.Login ?? string.Empty,
            AccountType = i.Account?.Type ?? string.Empty,
            AccountId = i.Account?.Id ?? 0,
            AvatarUrl = i.Account?.AvatarUrl
        }).ToList();
    }

    public async Task<string> GetInstallationTokenAsync(long installationId, CancellationToken cancellationToken = default)
    {
        // Check cache in DB first
        var installation = await _context.GitHubAppInstallations
            .FirstOrDefaultAsync(i => i.InstallationId == installationId, cancellationToken);

        if (installation?.CachedAccessToken != null &&
            installation.TokenExpiresAt.HasValue &&
            installation.TokenExpiresAt.Value > DateTime.UtcNow.AddMinutes(5))
        {
            _logger.LogDebug("Using cached installation token for installation {InstallationId}", installationId);
            return installation.CachedAccessToken;
        }

        // Generate fresh token
        var jwt = GenerateJwt();
        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await client.PostAsync(
            $"/app/installations/{installationId}/access_tokens",
            null,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var tokenResponse = JsonSerializer.Deserialize<GitHubTokenResponse>(json, JsonOptions);

        if (tokenResponse?.Token == null)
            throw new InvalidOperationException($"Failed to get installation token for installation {installationId}");

        // Cache the token
        if (installation != null)
        {
            installation.CachedAccessToken = tokenResponse.Token;
            installation.TokenExpiresAt = tokenResponse.ExpiresAt;
            installation.UpdateTimestamp();
            await _context.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Generated fresh installation token for installation {InstallationId}, expires at {ExpiresAt}",
            installationId, tokenResponse.ExpiresAt);

        return tokenResponse.Token;
    }

    public async Task<GitHubRepoListResult> ListInstallationReposAsync(
        long installationId, int page = 1, int perPage = 30, CancellationToken cancellationToken = default)
    {
        var token = await GetInstallationTokenAsync(installationId, cancellationToken);

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(
            $"/installation/repositories?per_page={perPage}&page={page}",
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<GitHubRepoListResponse>(json, JsonOptions);

        return new GitHubRepoListResult
        {
            TotalCount = result?.TotalCount ?? 0,
            Repositories = result?.Repositories?.Select(r => new GitHubInstallationRepo
            {
                Id = r.Id,
                FullName = r.FullName ?? string.Empty,
                Name = r.Name ?? string.Empty,
                Owner = r.Owner?.Login ?? string.Empty,
                Private = r.Private,
                Description = r.Description,
                Language = r.Language,
                StargazersCount = r.StargazersCount,
                ForksCount = r.ForksCount,
                DefaultBranch = r.DefaultBranch ?? "main",
                CloneUrl = r.CloneUrl ?? string.Empty,
                HtmlUrl = r.HtmlUrl ?? string.Empty
            }).ToList() ?? new()
        };
    }

    // Internal DTOs for GitHub API responses
    private class GitHubInstallationResponse
    {
        public long Id { get; set; }
        public GitHubAccountResponse? Account { get; set; }
    }

    private class GitHubAccountResponse
    {
        public long Id { get; set; }
        public string? Login { get; set; }
        public string? Type { get; set; }
        public string? AvatarUrl { get; set; }
    }

    private class GitHubTokenResponse
    {
        public string? Token { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    private class GitHubRepoListResponse
    {
        public int TotalCount { get; set; }
        public List<GitHubRepoResponse>? Repositories { get; set; }
    }

    private class GitHubRepoResponse
    {
        public long Id { get; set; }
        public string? FullName { get; set; }
        public string? Name { get; set; }
        public GitHubOwnerResponse? Owner { get; set; }
        public bool Private { get; set; }
        public string? Description { get; set; }
        public string? Language { get; set; }
        public int StargazersCount { get; set; }
        public int ForksCount { get; set; }
        public string? DefaultBranch { get; set; }
        public string? CloneUrl { get; set; }
        public string? HtmlUrl { get; set; }
    }

    private class GitHubOwnerResponse
    {
        public string? Login { get; set; }
    }
}
