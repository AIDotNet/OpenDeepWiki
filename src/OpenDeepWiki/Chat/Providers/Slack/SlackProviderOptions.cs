namespace OpenDeepWiki.Chat.Providers.Slack;

/// <summary>
/// Slack Provider configuration options
/// </summary>
public class SlackProviderOptions : ProviderOptions
{
    /// <summary>
    /// Slack Bot Token (xoxb-...)
    /// Used for all Slack Web API calls (chat.postMessage, etc.)
    /// Long-lived token that does not expire.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Slack Signing Secret
    /// Used to verify incoming webhook requests via HMAC-SHA256
    /// </summary>
    public string SigningSecret { get; set; } = string.Empty;

    /// <summary>
    /// Slack App-level Token (xapp-..., optional)
    /// Only needed for Socket Mode; not required for HTTP webhook mode
    /// </summary>
    public string? AppLevelToken { get; set; }

    /// <summary>
    /// Slack Web API base URL
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://slack.com/api";

    /// <summary>
    /// Whether to reply in threads when the incoming message is in a channel.
    /// Keeps conversations organized and reduces channel noise.
    /// </summary>
    public bool ReplyInThread { get; set; } = true;

    /// <summary>
    /// Maximum age in seconds for webhook request timestamps.
    /// Slack recommends rejecting requests older than 5 minutes to prevent replay attacks.
    /// </summary>
    public int WebhookTimestampToleranceSeconds { get; set; } = 300;
}
