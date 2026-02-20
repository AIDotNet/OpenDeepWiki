using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Chat.Providers.Slack;
using OpenDeepWiki.EFCore;

namespace OpenDeepWiki.Chat.Execution;

/// <summary>
/// Resolves messaging platform user IDs to DeepWiki user IDs via email matching.
/// </summary>
public interface IChatUserResolver
{
    /// <summary>
    /// Resolves a platform user ID to a DeepWiki user ID.
    /// Returns null if the user cannot be mapped.
    /// </summary>
    Task<string?> ResolveDeepWikiUserIdAsync(string platformUserId, string platform, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves Slack (and other platform) user IDs to DeepWiki user IDs by email matching.
/// Singleton service with in-memory caching.
/// </summary>
public class ChatUserResolver : IChatUserResolver
{
    private readonly IContextFactory _contextFactory;
    private readonly SlackProviderOptions _slackOptions;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ChatUserResolver> _logger;

    // Cache: "platform:userId" -> DeepWiki user ID (or null for unmapped)
    private readonly ConcurrentDictionary<string, CachedResolution> _cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    public ChatUserResolver(
        IContextFactory contextFactory,
        IOptions<SlackProviderOptions> slackOptions,
        HttpClient httpClient,
        ILogger<ChatUserResolver> logger)
    {
        _contextFactory = contextFactory;
        _slackOptions = slackOptions.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string?> ResolveDeepWikiUserIdAsync(
        string platformUserId,
        string platform,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(platformUserId) || string.IsNullOrWhiteSpace(platform))
            return null;

        var cacheKey = $"{platform}:{platformUserId}";

        // Check cache (with TTL)
        if (_cache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Cache hit for {CacheKey}: {UserId}", cacheKey, cached.DeepWikiUserId ?? "(unmapped)");
            return cached.DeepWikiUserId;
        }

        // Resolve based on platform
        string? deepWikiUserId = null;
        try
        {
            var email = platform.ToLowerInvariant() switch
            {
                "slack" => await GetSlackUserEmailAsync(platformUserId, cancellationToken),
                _ => null
            };

            if (!string.IsNullOrEmpty(email))
            {
                deepWikiUserId = await FindDeepWikiUserByEmailAsync(email, cancellationToken);

                if (deepWikiUserId != null)
                {
                    _logger.LogInformation(
                        "Mapped {Platform} user {PlatformUserId} ({Email}) to DeepWiki user {DeepWikiUserId}",
                        platform, platformUserId, email, deepWikiUserId);
                }
                else
                {
                    _logger.LogDebug(
                        "No DeepWiki account found for {Platform} user {PlatformUserId} ({Email})",
                        platform, platformUserId, email);
                }
            }
            else
            {
                _logger.LogDebug(
                    "Could not resolve email for {Platform} user {PlatformUserId}",
                    platform, platformUserId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to resolve {Platform} user {PlatformUserId} to DeepWiki user",
                platform, platformUserId);
        }

        // Cache the result (even null = "unmapped")
        _cache[cacheKey] = new CachedResolution(deepWikiUserId, DateTimeOffset.UtcNow.Add(CacheDuration));
        return deepWikiUserId;
    }

    /// <summary>
    /// Calls Slack users.info API to get the user's email address.
    /// Requires users:read.email OAuth scope on the Slack App.
    /// </summary>
    private async Task<string?> GetSlackUserEmailAsync(string slackUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_slackOptions.BotToken))
        {
            _logger.LogWarning("Slack BotToken not configured, cannot resolve user email");
            return null;
        }

        var url = $"{_slackOptions.ApiBaseUrl}/users.info?user={slackUserId}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _slackOptions.BotToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var userInfoResponse = JsonSerializer.Deserialize<SlackUserInfoResponse>(content);

        if (userInfoResponse?.Ok != true)
        {
            _logger.LogWarning("Slack users.info failed for {UserId}: {Error}",
                slackUserId, userInfoResponse?.Error);
            return null;
        }

        var email = userInfoResponse.UserInfo?.Profile?.Email;
        if (string.IsNullOrEmpty(email))
        {
            _logger.LogDebug("No email found for Slack user {UserId}. " +
                "Ensure the Slack App has users:read.email scope.", slackUserId);
        }

        return email;
    }

    /// <summary>
    /// Finds a DeepWiki user by email address.
    /// </summary>
    private async Task<string?> FindDeepWikiUserByEmailAsync(string email, CancellationToken cancellationToken)
    {
        using var context = _contextFactory.CreateContext();
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted && u.Status == 1, cancellationToken);
        return user?.Id;
    }

    private record CachedResolution(string? DeepWikiUserId, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
