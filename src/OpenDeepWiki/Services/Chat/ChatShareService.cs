using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Chat;

/// <summary>
/// 分享内容中的 Token 使用情况
/// </summary>
public class ChatShareTokenUsageDto
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

/// <summary>
/// 分享内容中的工具结果
/// </summary>
public class ChatShareToolResultDto
{
    public string ToolCallId { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public bool IsError { get; set; }
}

/// <summary>
/// 分享内容中的工具调用
/// </summary>
public class ChatShareToolCallDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object>? Arguments { get; set; }
}

/// <summary>
/// 分享内容中的引用文本
/// </summary>
public class ChatShareQuotedTextDto
{
    public string? Title { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// 分享内容块
/// </summary>
public class ChatShareContentBlockDto
{
    public string Type { get; set; } = "text";
    public string? Content { get; set; }
    public ChatShareToolCallDto? ToolCall { get; set; }
}

/// <summary>
/// 分享的对话消息
/// </summary>
public class ChatShareMessageDto
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Thinking { get; set; }
    public List<ChatShareContentBlockDto>? ContentBlocks { get; set; }
    public List<string>? Images { get; set; }
    public ChatShareQuotedTextDto? QuotedText { get; set; }
    public List<ChatShareToolCallDto>? ToolCalls { get; set; }
    public ChatShareToolResultDto? ToolResult { get; set; }
    public ChatShareTokenUsageDto? TokenUsage { get; set; }
    public long Timestamp { get; set; }
}

/// <summary>
/// 创建分享请求
/// </summary>
public class CreateChatShareRequest
{
    public List<ChatShareMessageDto> Messages { get; set; } = new();
    public DocContextDto Context { get; set; } = new();
    public string ModelId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int? ExpireMinutes { get; set; }
    [JsonIgnore]
    public string? CreatedBy { get; set; }
}

/// <summary>
/// 分享详情响应
/// </summary>
public class ChatShareResponse
{
    public string ShareId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DocContextDto Context { get; set; } = new();
    public string ModelId { get; set; } = string.Empty;
    public IReadOnlyList<ChatShareMessageDto> Messages { get; set; } = Array.Empty<ChatShareMessageDto>();
}

/// <summary>
/// 聊天分享服务接口
/// </summary>
public interface IChatShareService
{
    Task<ChatShareResponse> CreateShareAsync(CreateChatShareRequest request, CancellationToken cancellationToken = default);
    Task<ChatShareResponse?> GetShareAsync(string shareId, CancellationToken cancellationToken = default);
    Task<bool> RevokeShareAsync(string shareId, string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 聊天分享服务实现
/// </summary>
public class ChatShareService : IChatShareService
{
    private const int DefaultExpireMinutes = 60 * 24 * 30; // 30天
    private const int MaxMessages = 400;
    private const int MaxSnapshotSizeBytes = 512 * 1024; // 512KB

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IContext _context;
    private readonly ILogger<ChatShareService> _logger;

    public ChatShareService(IContext context, ILogger<ChatShareService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ChatShareResponse> CreateShareAsync(CreateChatShareRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Messages.Count == 0)
        {
            throw new InvalidOperationException("Cannot share an empty conversation");
        }

        if (request.Messages.Count > MaxMessages)
        {
            throw new InvalidOperationException($"Maximum {MaxMessages} messages per share");
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new InvalidOperationException("Missing model information, cannot create share");
        }

        var snapshotPayload = new ChatShareSnapshotPayload
        {
            ModelId = request.ModelId,
            Context = request.Context,
            Messages = request.Messages
        };

        var snapshotJson = JsonSerializer.Serialize(snapshotPayload, SerializerOptions);
        if (snapshotJson.Length > MaxSnapshotSizeBytes)
        {
            throw new InvalidOperationException("Share content too large, please reduce the conversation and try again");
        }

        var title = !string.IsNullOrWhiteSpace(request.Title)
            ? request.Title!
            : BuildDefaultTitle(request.Messages);

        var expiresAt = request.ExpireMinutes.HasValue && request.ExpireMinutes > 0
            ? DateTime.UtcNow.AddMinutes(request.ExpireMinutes.Value)
            : DateTime.UtcNow.AddMinutes(DefaultExpireMinutes);

        var snapshot = new ChatShareSnapshot
        {
            Id = Guid.NewGuid(),
            ShareId = await GenerateShareIdAsync(cancellationToken),
            Title = title,
            Description = request.Description,
            CreatedBy = request.CreatedBy,
            SnapshotJson = snapshotJson,
            Metadata = JsonSerializer.Serialize(new
            {
                request.ModelId,
                request.Description
            }, SerializerOptions),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };

        _context.ChatShareSnapshots.Add(snapshot);
        await _context.SaveChangesAsync(cancellationToken);

        return new ChatShareResponse
        {
            ShareId = snapshot.ShareId,
            Title = snapshot.Title,
            Description = snapshot.Description,
            CreatedAt = snapshot.CreatedAt,
            ExpiresAt = snapshot.ExpiresAt,
            Context = snapshotPayload.Context,
            ModelId = snapshotPayload.ModelId,
            Messages = new ReadOnlyCollection<ChatShareMessageDto>(snapshotPayload.Messages)
        };
    }

    public async Task<ChatShareResponse?> GetShareAsync(string shareId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shareId);

        var snapshot = await _context.ChatShareSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ShareId == shareId, cancellationToken);

        if (snapshot == null)
        {
            return null;
        }

        if (snapshot.RevokedAt.HasValue)
        {
            return null;
        }

        if (snapshot.ExpiresAt.HasValue && snapshot.ExpiresAt.Value < DateTime.UtcNow)
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<ChatShareSnapshotPayload>(snapshot.SnapshotJson, SerializerOptions);
        if (payload == null)
        {
            _logger.LogWarning("分享 {ShareId} 的快照数据无法解析", shareId);
            return null;
        }

        return new ChatShareResponse
        {
            ShareId = snapshot.ShareId,
            Title = snapshot.Title,
            Description = snapshot.Description,
            CreatedAt = snapshot.CreatedAt,
            ExpiresAt = snapshot.ExpiresAt,
            Context = payload.Context,
            ModelId = payload.ModelId,
            Messages = new ReadOnlyCollection<ChatShareMessageDto>(payload.Messages)
        };
    }

    public async Task<bool> RevokeShareAsync(string shareId, string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shareId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var snapshot = await _context.ChatShareSnapshots
            .FirstOrDefaultAsync(s => s.ShareId == shareId, cancellationToken);

        if (snapshot == null)
        {
            return false;
        }

        if (!string.Equals(snapshot.CreatedBy, userId, StringComparison.Ordinal))
        {
            return false;
        }

        if (snapshot.RevokedAt.HasValue)
        {
            return true;
        }

        snapshot.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string BuildDefaultTitle(IReadOnlyCollection<ChatShareMessageDto> messages)
    {
        var firstUser = messages.FirstOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
        if (firstUser?.Content is { Length: > 0 } content)
        {
            return content.Length > 40 ? content[..40] + "…" : content;
        }

        return "AI Conversation Share";
    }

    private async Task<string> GenerateShareIdAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 5; i++)
        {
            var bytes = RandomNumberGenerator.GetBytes(12);
            var candidate = Convert.ToHexString(bytes).ToLowerInvariant();
            var exists = await _context.ChatShareSnapshots
                .AnyAsync(s => s.ShareId == candidate, cancellationToken);
            if (!exists)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to generate unique share ID, please try again later");
    }

    private class ChatShareSnapshotPayload
    {
        public string ModelId { get; set; } = string.Empty;
        public DocContextDto Context { get; set; } = new();
        public List<ChatShareMessageDto> Messages { get; set; } = new();
    }
}
