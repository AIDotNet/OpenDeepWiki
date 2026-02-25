using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Chat.Abstractions;
using OpenDeepWiki.Chat.Config;

namespace OpenDeepWiki.Chat.Providers.Slack;

/// <summary>
/// Slack messaging Provider implementation.
/// Uses Slack Events API for receiving messages and Web API for sending.
/// Supports runtime configuration from database via IConfigurableProvider.
/// </summary>
public class SlackProvider : BaseMessageProvider, IConfigurableProvider
{
    private readonly HttpClient _httpClient;
    private readonly SlackProviderOptions _envOptions;
    private volatile SlackProviderOptions _activeOptions;
    private bool? _dbIsEnabled;
    private string? _botUserId;

    private static readonly HashSet<ChatMessageType> SupportedMessageTypes = new()
    {
        ChatMessageType.Text,
        ChatMessageType.Image,
        ChatMessageType.RichText,
        ChatMessageType.Card
    };

    private static readonly HashSet<string> IgnoredSubtypes = new()
    {
        "bot_message", "message_changed", "message_deleted",
        "channel_join", "channel_leave", "channel_topic",
        "channel_purpose", "channel_name", "file_comment",
        "channel_archive", "channel_unarchive", "group_join",
        "group_leave", "group_topic", "group_purpose", "group_name"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public override string PlatformId => "slack";
    public override string DisplayName => "Slack";
    public override bool IsEnabled => _dbIsEnabled ?? base.IsEnabled;
    public string ConfigSource { get; private set; } = "environment";

    /// <summary>
    /// The currently active Bot Token (from DB config or env vars).
    /// Used by ChatUserResolver to call Slack APIs for user identity resolution.
    /// </summary>
    public string ActiveBotToken => _activeOptions.BotToken;

    /// <summary>
    /// The currently active API base URL (from DB config or env vars).
    /// </summary>
    public string ActiveApiBaseUrl => _activeOptions.ApiBaseUrl;

    public SlackProvider(
        ILogger<SlackProvider> logger,
        IOptions<SlackProviderOptions> options,
        HttpClient httpClient)
        : base(logger, options)
    {
        _httpClient = httpClient;
        _envOptions = CloneOptions(options.Value);
        _activeOptions = options.Value;
    }

    #region IConfigurableProvider

    /// <summary>
    /// Apply configuration from database. Creates a new options instance and swaps
    /// the volatile reference atomically for thread safety.
    /// </summary>
    public void ApplyConfig(ProviderConfigDto config)
    {
        var newOptions = new SlackProviderOptions
        {
            Enabled = config.IsEnabled,
            MessageInterval = config.MessageInterval,
            MaxRetryCount = config.MaxRetryCount,
            RetryDelayBase = _activeOptions.RetryDelayBase,
            RequestTimeout = _activeOptions.RequestTimeout,
        };

        if (!string.IsNullOrEmpty(config.ConfigData))
        {
            try
            {
                var configDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(config.ConfigData);
                if (configDict != null)
                {
                    if (configDict.TryGetValue("BotToken", out var botToken))
                        newOptions.BotToken = botToken.GetString() ?? string.Empty;
                    if (configDict.TryGetValue("SigningSecret", out var signingSecret))
                        newOptions.SigningSecret = signingSecret.GetString() ?? string.Empty;
                    if (configDict.TryGetValue("AppLevelToken", out var appToken))
                        newOptions.AppLevelToken = appToken.GetString();
                    if (configDict.TryGetValue("ApiBaseUrl", out var apiBase) && !string.IsNullOrEmpty(apiBase.GetString()))
                        newOptions.ApiBaseUrl = apiBase.GetString()!;
                    if (configDict.TryGetValue("ReplyInThread", out var replyInThread))
                        newOptions.ReplyInThread = replyInThread.ValueKind == JsonValueKind.True;
                    if (configDict.TryGetValue("WebhookTimestampToleranceSeconds", out var tolerance) &&
                        tolerance.TryGetInt32(out var toleranceValue))
                        newOptions.WebhookTimestampToleranceSeconds = toleranceValue;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to parse Slack ConfigData JSON, keeping current config");
                return;
            }
        }

        _dbIsEnabled = config.IsEnabled;
        _activeOptions = newOptions;
        ConfigSource = "database";

        Logger.LogInformation("SlackProvider config applied from database. Enabled={Enabled}", config.IsEnabled);

        // Re-initialize to validate the new token and resolve bot user ID
        _ = Task.Run(async () =>
        {
            try { await InitializeAsync(); }
            catch (Exception ex) { Logger.LogError(ex, "Failed to re-initialize SlackProvider after config change"); }
        });
    }

    /// <summary>
    /// Reset to environment variable defaults (when DB config is deleted).
    /// </summary>
    public void ResetToDefaults()
    {
        _dbIsEnabled = null;
        _activeOptions = CloneOptions(_envOptions);
        ConfigSource = "environment";

        Logger.LogInformation("SlackProvider config reset to environment variable defaults");

        _ = Task.Run(async () =>
        {
            try { await InitializeAsync(); }
            catch (Exception ex) { Logger.LogError(ex, "Failed to re-initialize SlackProvider after config reset"); }
        });
    }

    private static SlackProviderOptions CloneOptions(SlackProviderOptions source) => new()
    {
        Enabled = source.Enabled,
        MessageInterval = source.MessageInterval,
        MaxRetryCount = source.MaxRetryCount,
        RetryDelayBase = source.RetryDelayBase,
        RequestTimeout = source.RequestTimeout,
        BotToken = source.BotToken,
        SigningSecret = source.SigningSecret,
        AppLevelToken = source.AppLevelToken,
        ApiBaseUrl = source.ApiBaseUrl,
        ReplyInThread = source.ReplyInThread,
        WebhookTimestampToleranceSeconds = source.WebhookTimestampToleranceSeconds
    };

    #endregion

    /// <summary>
    /// Initialize provider: verify bot token and resolve bot user ID.
    /// </summary>
    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await base.InitializeAsync(cancellationToken);

        var opts = _activeOptions;
        if (string.IsNullOrEmpty(opts.BotToken))
        {
            Logger.LogWarning("Slack BotToken not configured, provider will not be fully functional");
            return;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{opts.ApiBaseUrl}/auth.test");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.BotToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var authResponse = JsonSerializer.Deserialize<SlackAuthTestResponse>(content);

            if (authResponse?.Ok == true)
            {
                _botUserId = authResponse.UserId;
                Logger.LogInformation("Slack provider initialized. Bot user: {User} ({UserId})",
                    authResponse.User, _botUserId);
            }
            else
            {
                Logger.LogError("Slack auth.test failed: {Error}", authResponse?.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize Slack provider");
        }
    }

    /// <summary>
    /// Validate Slack webhook requests using HMAC-SHA256 signature verification.
    /// Also handles the url_verification challenge for initial app setup.
    /// </summary>
    public override async Task<WebhookValidationResult> ValidateWebhookAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var opts = _activeOptions;

            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync(cancellationToken);
            request.Body.Position = 0;

            // Parse body to check for url_verification challenge
            var envelope = JsonSerializer.Deserialize<SlackEventEnvelope>(body);
            if (envelope?.Type == "url_verification")
            {
                return new WebhookValidationResult(true, Challenge: envelope.Challenge);
            }

            // Verify request signature
            if (string.IsNullOrEmpty(opts.SigningSecret))
            {
                Logger.LogWarning("Slack SigningSecret not configured, skipping signature verification");
                return new WebhookValidationResult(true);
            }

            var timestamp = request.Headers["X-Slack-Request-Timestamp"].FirstOrDefault();
            var signature = request.Headers["X-Slack-Signature"].FirstOrDefault();

            if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature))
            {
                return new WebhookValidationResult(false,
                    ErrorMessage: "Missing X-Slack-Request-Timestamp or X-Slack-Signature headers");
            }

            // Reject requests older than tolerance (replay attack prevention)
            if (long.TryParse(timestamp, out var ts))
            {
                var requestAge = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts;
                if (Math.Abs(requestAge) > opts.WebhookTimestampToleranceSeconds)
                {
                    return new WebhookValidationResult(false,
                        ErrorMessage: "Request timestamp too old");
                }
            }

            // Compute expected signature: v0=HMAC-SHA256(signing_secret, "v0:{timestamp}:{body}")
            var sigBasestring = $"v0:{timestamp}:{body}";
            var computedSignature = ComputeSignature(opts.SigningSecret, sigBasestring);

            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSignature),
                Encoding.UTF8.GetBytes(signature)))
            {
                return new WebhookValidationResult(false, ErrorMessage: "Invalid signature");
            }

            return new WebhookValidationResult(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to validate Slack webhook");
            return new WebhookValidationResult(false, ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Parse Slack Events API event_callback payloads into unified IChatMessage format.
    /// </summary>
    public override Task<IChatMessage?> ParseMessageAsync(
        string rawMessage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<SlackEventEnvelope>(rawMessage);
            if (envelope == null)
            {
                Logger.LogWarning("Failed to deserialize Slack event envelope");
                return Task.FromResult<IChatMessage?>(null);
            }

            // Only process event_callback type
            if (envelope.Type != "event_callback")
            {
                Logger.LogDebug("Ignoring non-event_callback: {Type}", envelope.Type);
                return Task.FromResult<IChatMessage?>(null);
            }

            var evt = envelope.Event;
            if (evt == null)
            {
                Logger.LogWarning("No event content in Slack envelope");
                return Task.FromResult<IChatMessage?>(null);
            }

            // Event type filtering:
            // - app_mention: always process (channel @mentions)
            // - message: only process for DMs (im/mpim), skip for channels
            //   (channels already send app_mention, processing both causes duplicates)
            if (evt.Type == "message" && evt.ChannelType is "channel" or "group")
            {
                Logger.LogDebug("Ignoring message event in channel (app_mention handles these): {Channel}", evt.Channel);
                return Task.FromResult<IChatMessage?>(null);
            }

            if (evt.Type != "message" && evt.Type != "app_mention")
            {
                Logger.LogDebug("Ignoring event type: {Type}", evt.Type);
                return Task.FromResult<IChatMessage?>(null);
            }

            // Filter out bot's own messages
            if (!string.IsNullOrEmpty(evt.BotId))
            {
                Logger.LogDebug("Ignoring bot message from bot_id: {BotId}", evt.BotId);
                return Task.FromResult<IChatMessage?>(null);
            }

            if (!string.IsNullOrEmpty(_botUserId) && evt.User == _botUserId)
            {
                Logger.LogDebug("Ignoring own bot message");
                return Task.FromResult<IChatMessage?>(null);
            }

            // Filter out non-content subtypes
            if (!string.IsNullOrEmpty(evt.Subtype) && IgnoredSubtypes.Contains(evt.Subtype))
            {
                Logger.LogDebug("Ignoring message subtype: {Subtype}", evt.Subtype);
                return Task.FromResult<IChatMessage?>(null);
            }

            // Determine message type and content
            var (messageType, content) = ParseSlackMessageContent(evt);

            // Build metadata including thread context
            var metadata = new Dictionary<string, object>
            {
                { "channel", evt.Channel ?? string.Empty },
                { "channel_type", evt.ChannelType ?? string.Empty },
                { "event_type", evt.Type },
                { "team_id", envelope.TeamId ?? string.Empty },
                { "event_id", envelope.EventId ?? string.Empty }
            };

            // Thread context for reply routing
            if (!string.IsNullOrEmpty(evt.ThreadTs))
            {
                metadata["thread_ts"] = evt.ThreadTs;
            }
            else if (!string.IsNullOrEmpty(evt.Ts))
            {
                // Top-level message: use ts as potential thread parent
                metadata["message_ts"] = evt.Ts;
            }

            IChatMessage result = new ChatMessage
            {
                MessageId = envelope.EventId ?? evt.Ts ?? Guid.NewGuid().ToString(),
                SenderId = evt.User ?? string.Empty,
                ReceiverId = evt.Channel,
                Content = content,
                MessageType = messageType,
                Platform = PlatformId,
                Timestamp = ParseSlackTimestamp(evt.Ts),
                Metadata = metadata
            };

            return Task.FromResult<IChatMessage?>(result);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse Slack message");
            return Task.FromResult<IChatMessage?>(null);
        }
    }

    /// <summary>
    /// Send a message to Slack via chat.postMessage Web API.
    /// </summary>
    public override async Task<SendResult> SendMessageAsync(
        IChatMessage message,
        string targetUserId,
        CancellationToken cancellationToken = default)
    {
        var opts = _activeOptions;

        return await SendWithRetryAsync(opts, async () =>
        {
            var processedMessage = DegradeMessage(message, SupportedMessageTypes);

            var postRequest = new SlackPostMessageRequest
            {
                Channel = targetUserId
            };

            // Handle thread replies
            if (opts.ReplyInThread && message.Metadata != null)
            {
                if (message.Metadata.TryGetValue("thread_ts", out var threadTs))
                {
                    postRequest.ThreadTs = threadTs.ToString();
                }
                else if (message.Metadata.TryGetValue("message_ts", out var messageTs))
                {
                    postRequest.ThreadTs = messageTs.ToString();
                }
            }

            // Convert message content based on type
            switch (processedMessage.MessageType)
            {
                case ChatMessageType.Card:
                case ChatMessageType.RichText:
                    try
                    {
                        var blocks = JsonSerializer.Deserialize<List<SlackBlock>>(processedMessage.Content);
                        postRequest.Blocks = blocks;
                        postRequest.Text = string.Empty; // Fallback text required by Slack
                    }
                    catch
                    {
                        // If blocks parsing fails, send as text
                        postRequest.Text = processedMessage.Content;
                    }
                    break;

                case ChatMessageType.Text:
                default:
                    postRequest.Text = processedMessage.Content;
                    break;
            }

            var url = $"{opts.ApiBaseUrl}/chat.postMessage";
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(postRequest, SerializerOptions),
                    Encoding.UTF8,
                    "application/json")
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.BotToken);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<SlackPostMessageResponse>(responseContent);

            if (apiResponse?.Ok == true)
            {
                return new SendResult(true, apiResponse.Ts);
            }

            var shouldRetry = IsRetryableError(apiResponse?.Error, response.StatusCode);
            return new SendResult(
                false,
                ErrorCode: apiResponse?.Error ?? response.StatusCode.ToString(),
                ErrorMessage: apiResponse?.Error,
                ShouldRetry: shouldRetry);
        }, cancellationToken);
    }

    #region Private methods

    private static (ChatMessageType Type, string Content) ParseSlackMessageContent(SlackEvent evt)
    {
        // If message has image files, treat as Image type
        if (evt.Files != null && evt.Files.Count > 0)
        {
            var firstFile = evt.Files[0];
            if (firstFile.Mimetype?.StartsWith("image/") == true)
            {
                return (ChatMessageType.Image, firstFile.UrlPrivate ?? firstFile.Id);
            }
            return (ChatMessageType.File, firstFile.UrlPrivate ?? firstFile.Id);
        }

        return (ChatMessageType.Text, evt.Text ?? string.Empty);
    }

    private static DateTimeOffset ParseSlackTimestamp(string? ts)
    {
        // Slack timestamps are Unix epoch with microseconds: "1234567890.123456"
        if (!string.IsNullOrEmpty(ts) && double.TryParse(ts,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var unixTimestamp))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(unixTimestamp * 1000));
        }
        return DateTimeOffset.UtcNow;
    }

    private static string ComputeSignature(string signingSecret, string basestring)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(basestring));
        return $"v0={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static bool IsRetryableError(string? error, System.Net.HttpStatusCode statusCode)
    {
        if (statusCode == System.Net.HttpStatusCode.TooManyRequests)
            return true;

        return error switch
        {
            "ratelimited" => true,
            "service_unavailable" => true,
            "request_timeout" => true,
            "internal_error" => true,
            "fatal_error" => true,
            _ => false
        };
    }

    private async Task<SendResult> SendWithRetryAsync(
        SlackProviderOptions opts,
        Func<Task<SendResult>> sendFunc,
        CancellationToken cancellationToken)
    {
        var maxRetries = opts.MaxRetryCount;
        var retryDelayBase = opts.RetryDelayBase;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var result = await sendFunc();

                if (result.Success || !result.ShouldRetry || attempt >= maxRetries)
                    return result;

                var delay = retryDelayBase * (int)Math.Pow(2, attempt);
                Logger.LogWarning(
                    "Slack API call failed (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms. Error: {Error}",
                    attempt + 1, maxRetries + 1, delay, result.ErrorMessage);

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                var delay = retryDelayBase * (int)Math.Pow(2, attempt);
                Logger.LogWarning(ex,
                    "Slack API call threw exception (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms",
                    attempt + 1, maxRetries + 1, delay);

                await Task.Delay(delay, cancellationToken);
            }
        }

        return new SendResult(false, ErrorCode: "MAX_RETRIES_EXCEEDED",
            ErrorMessage: "Maximum retry attempts exceeded");
    }

    #endregion

    #region Block Kit helper methods

    /// <summary>
    /// Create a simple markdown text message using Block Kit section block
    /// </summary>
    public static string CreateTextBlocks(string text)
    {
        var blocks = new List<SlackBlock>
        {
            new()
            {
                Type = "section",
                Text = new SlackTextObject
                {
                    Type = "mrkdwn",
                    Text = text
                }
            }
        };
        return JsonSerializer.Serialize(blocks);
    }

    /// <summary>
    /// Create a multi-section message with a header and dividers
    /// </summary>
    public static string CreateMultiSectionBlocks(string header, IEnumerable<string> sections)
    {
        var blocks = new List<SlackBlock>
        {
            new()
            {
                Type = "header",
                Text = new SlackTextObject
                {
                    Type = "plain_text",
                    Text = header
                }
            }
        };

        foreach (var section in sections)
        {
            blocks.Add(new SlackBlock { Type = "divider" });
            blocks.Add(new SlackBlock
            {
                Type = "section",
                Text = new SlackTextObject
                {
                    Type = "mrkdwn",
                    Text = section
                }
            });
        }

        return JsonSerializer.Serialize(blocks);
    }

    #endregion
}
