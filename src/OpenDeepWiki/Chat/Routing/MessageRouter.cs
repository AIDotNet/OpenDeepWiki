using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenDeepWiki.Chat.Abstractions;
using OpenDeepWiki.Chat.Callbacks;
using OpenDeepWiki.Chat.Providers;
using OpenDeepWiki.Chat.Queue;

namespace OpenDeepWiki.Chat.Routing;

/// <summary>
/// 消息路由器实现
/// 负责将消息路由到正确的 Provider，支持 Provider 注册和消息路由
/// Registered as Singleton; uses IServiceScopeFactory to resolve scoped dependencies.
/// </summary>
public class MessageRouter : IMessageRouter
{
    private readonly ILogger<MessageRouter> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, IMessageProvider> _providers;
    private readonly MessageRouterOptions _options;

    public MessageRouter(
        ILogger<MessageRouter> logger,
        IServiceScopeFactory scopeFactory,
        MessageRouterOptions? options = null)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _providers = new ConcurrentDictionary<string, IMessageProvider>(StringComparer.OrdinalIgnoreCase);
        _options = options ?? new MessageRouterOptions();
    }

    /// <inheritdoc />
    public async Task RouteIncomingAsync(IChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var platform = message.Platform;
        if (string.IsNullOrWhiteSpace(platform))
        {
            _logger.LogWarning("Message {MessageId} has no platform specified", message.MessageId);
            throw new ArgumentException("Message platform cannot be empty", nameof(message));
        }

        var provider = GetProvider(platform);
        if (provider == null)
        {
            _logger.LogWarning("No provider registered for platform: {Platform}", platform);
            throw new InvalidOperationException($"No provider registered for platform: {platform}");
        }

        if (!provider.IsEnabled)
        {
            _logger.LogWarning("Provider {Platform} is disabled, message {MessageId} will not be processed",
                platform, message.MessageId);
            return;
        }

        _logger.LogDebug("Routing incoming message {MessageId} from platform {Platform}",
            message.MessageId, platform);

        // Enqueue message for async processing by ChatMessageProcessingWorker.
        // Session management is handled by the worker to avoid duplicate history entries.
        using var scope = _scopeFactory.CreateScope();
        var messageQueue = scope.ServiceProvider.GetRequiredService<IMessageQueue>();

        var queuedMessage = new QueuedMessage(
            Id: Guid.NewGuid().ToString(),
            Message: message,
            SessionId: string.Empty, // Worker will resolve session
            TargetUserId: message.SenderId,
            Type: QueuedMessageType.Incoming
        );

        await messageQueue.EnqueueAsync(queuedMessage, cancellationToken);
        _logger.LogDebug("Message {MessageId} enqueued for processing", message.MessageId);
    }


    /// <inheritdoc />
    public async Task RouteOutgoingAsync(IChatMessage message, string targetUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUserId);

        var platform = message.Platform;
        if (string.IsNullOrWhiteSpace(platform))
        {
            _logger.LogWarning("Outgoing message {MessageId} has no platform specified", message.MessageId);
            throw new ArgumentException("Message platform cannot be empty", nameof(message));
        }

        var provider = GetProvider(platform);
        if (provider == null)
        {
            _logger.LogWarning("No provider registered for platform: {Platform}", platform);
            throw new InvalidOperationException($"No provider registered for platform: {platform}");
        }

        if (!provider.IsEnabled)
        {
            _logger.LogWarning("Provider {Platform} is disabled, message {MessageId} will not be sent",
                platform, message.MessageId);
            return;
        }

        _logger.LogDebug("Routing outgoing message {MessageId} to user {UserId} on platform {Platform}",
            message.MessageId, targetUserId, platform);

        // Create a scope to resolve scoped dependencies (MessageCallback)
        using var scope = _scopeFactory.CreateScope();
        var messageCallback = scope.ServiceProvider.GetRequiredService<IMessageCallback>();

        // 通过回调管理器发送消息
        await messageCallback.SendAsync(platform, targetUserId, message, cancellationToken);
    }

    /// <inheritdoc />
    public IMessageProvider? GetProvider(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return null;
        }

        _providers.TryGetValue(platform, out var provider);
        return provider;
    }

    /// <inheritdoc />
    public IEnumerable<IMessageProvider> GetAllProviders()
    {
        return _providers.Values.ToList();
    }

    /// <inheritdoc />
    public void RegisterProvider(IMessageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.PlatformId))
        {
            throw new ArgumentException("Provider PlatformId cannot be empty", nameof(provider));
        }

        if (_providers.TryAdd(provider.PlatformId, provider))
        {
            _logger.LogInformation("Provider {PlatformId} ({DisplayName}) registered successfully",
                provider.PlatformId, provider.DisplayName);
        }
        else
        {
            // 如果已存在，则更新
            _providers[provider.PlatformId] = provider;
            _logger.LogInformation("Provider {PlatformId} ({DisplayName}) updated",
                provider.PlatformId, provider.DisplayName);
        }
    }

    /// <inheritdoc />
    public bool UnregisterProvider(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return false;
        }

        if (_providers.TryRemove(platform, out var provider))
        {
            _logger.LogInformation("Provider {PlatformId} ({DisplayName}) unregistered",
                provider.PlatformId, provider.DisplayName);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool HasProvider(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return false;
        }

        return _providers.ContainsKey(platform);
    }
}

/// <summary>
/// MessageRouter 配置选项
/// </summary>
public class MessageRouterOptions
{
    /// <summary>
    /// 是否启用消息日志
    /// </summary>
    public bool EnableMessageLogging { get; set; } = true;

    /// <summary>
    /// 路由超时时间（毫秒）
    /// </summary>
    public int RouteTimeoutMs { get; set; } = 30000;
}
