namespace OpenDeepWiki.Chat.Abstractions;

/// <summary>
/// 统一消息实现
/// </summary>
public class ChatMessage : IChatMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string SenderId { get; init; } = string.Empty;
    public string? ReceiverId { get; init; }
    public string Content { get; init; } = string.Empty;
    public ChatMessageType MessageType { get; init; } = ChatMessageType.Text;
    public string Platform { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public IDictionary<string, object>? Metadata { get; set; }
}
