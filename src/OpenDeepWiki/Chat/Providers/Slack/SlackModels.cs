using System.Text.Json.Serialization;

namespace OpenDeepWiki.Chat.Providers.Slack;

#region Events API Models

/// <summary>
/// Slack Events API envelope.
/// Handles both url_verification challenges and event_callback payloads.
/// </summary>
public class SlackEventEnvelope
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("challenge")]
    public string? Challenge { get; set; }

    [JsonPropertyName("team_id")]
    public string? TeamId { get; set; }

    [JsonPropertyName("api_app_id")]
    public string? ApiAppId { get; set; }

    [JsonPropertyName("event")]
    public SlackEvent? Event { get; set; }

    [JsonPropertyName("event_id")]
    public string? EventId { get; set; }

    [JsonPropertyName("event_time")]
    public long EventTime { get; set; }
}

/// <summary>
/// Slack event payload within the envelope
/// </summary>
public class SlackEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("subtype")]
    public string? Subtype { get; set; }

    [JsonPropertyName("user")]
    public string? User { get; set; }

    [JsonPropertyName("bot_id")]
    public string? BotId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("ts")]
    public string? Ts { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("channel_type")]
    public string? ChannelType { get; set; }

    [JsonPropertyName("thread_ts")]
    public string? ThreadTs { get; set; }

    [JsonPropertyName("files")]
    public List<SlackFile>? Files { get; set; }
}

/// <summary>
/// Slack file attachment within a message event
/// </summary>
public class SlackFile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("mimetype")]
    public string? Mimetype { get; set; }

    [JsonPropertyName("url_private")]
    public string? UrlPrivate { get; set; }

    [JsonPropertyName("url_private_download")]
    public string? UrlPrivateDownload { get; set; }
}

#endregion

#region Block Kit Models

/// <summary>
/// Slack Block Kit block element
/// </summary>
public class SlackBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public SlackTextObject? Text { get; set; }

    [JsonPropertyName("elements")]
    public List<SlackBlock>? Elements { get; set; }

    [JsonPropertyName("block_id")]
    public string? BlockId { get; set; }
}

/// <summary>
/// Slack text object used in blocks
/// </summary>
public class SlackTextObject
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "mrkdwn";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

#endregion

#region Web API Request Models

/// <summary>
/// Request body for chat.postMessage
/// </summary>
public class SlackPostMessageRequest
{
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("blocks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SlackBlock>? Blocks { get; set; }

    [JsonPropertyName("thread_ts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadTs { get; set; }

    [JsonPropertyName("reply_broadcast")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReplyBroadcast { get; set; }

    [JsonPropertyName("mrkdwn")]
    public bool Mrkdwn { get; set; } = true;

    [JsonPropertyName("unfurl_links")]
    public bool UnfurlLinks { get; set; }
}

#endregion

#region Web API Response Models

/// <summary>
/// Standard Slack API response envelope
/// </summary>
public class SlackApiResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Response from chat.postMessage
/// </summary>
public class SlackPostMessageResponse : SlackApiResponse
{
    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("ts")]
    public string? Ts { get; set; }
}

/// <summary>
/// Response from auth.test (used at initialization)
/// </summary>
public class SlackAuthTestResponse : SlackApiResponse
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("team")]
    public string? Team { get; set; }

    [JsonPropertyName("user")]
    public string? User { get; set; }

    [JsonPropertyName("team_id")]
    public string? TeamId { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("bot_id")]
    public string? BotId { get; set; }
}

#endregion

#region Users API Response Models

/// <summary>
/// Response from users.info API (used for identity resolution)
/// </summary>
public class SlackUserInfoResponse : SlackApiResponse
{
    [JsonPropertyName("user")]
    public SlackUserInfo? UserInfo { get; set; }
}

/// <summary>
/// Slack user object from users.info
/// </summary>
public class SlackUserInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("real_name")]
    public string? RealName { get; set; }

    [JsonPropertyName("profile")]
    public SlackUserProfile? Profile { get; set; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; set; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }
}

/// <summary>
/// Slack user profile containing email
/// </summary>
public class SlackUserProfile
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("real_name")]
    public string? RealName { get; set; }
}

#endregion
