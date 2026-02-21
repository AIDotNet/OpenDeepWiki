using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Chat.Abstractions;
using OpenDeepWiki.Chat.Callbacks;
using OpenDeepWiki.Chat.Execution;
using OpenDeepWiki.Chat.Queue;
using OpenDeepWiki.Chat.Sessions;
using System.Threading.Channels;

namespace OpenDeepWiki.Chat.Processing;

/// <summary>
/// 消息处理后台服务
/// 负责从队列中取出消息、处理并发送回调
/// Requirements: 10.1, 10.2, 10.3
/// </summary>
public class ChatMessageProcessingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChatMessageProcessingWorker> _logger;
    private readonly ChatProcessingOptions _options;

    private readonly Channel<QueuedMessage> _channel;

    public ChatMessageProcessingWorker(
        IServiceProvider serviceProvider,
        ILogger<ChatMessageProcessingWorker> logger,
        IOptions<ChatProcessingOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;

        var capacity = Math.Max(1, _options.MaxConcurrency) * 100;
        _channel = Channel.CreateBounded<QueuedMessage>(new BoundedChannelOptions(capacity)
        {
            SingleWriter = false,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var concurrency = Math.Max(1, _options.MaxConcurrency);

        _logger.LogInformation("消息处理 Worker 已启动，并发数: {Concurrency}", concurrency);

        var producer = ProduceMessagesAsync(stoppingToken);

        var consumers = Enumerable.Range(0, concurrency)
            .Select(_ => ConsumeMessagesAsync(stoppingToken))
            .ToArray();

        await Task.WhenAll(consumers.Prepend(producer));

        _logger.LogInformation("消息处理 Worker 已停止");
    }

    private async Task ProduceMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageQueue = scope.ServiceProvider.GetRequiredService<IMessageQueue>();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var msg = await messageQueue.DequeueAsync(stoppingToken);
                if (msg != null)
                    await _channel.Writer.WriteAsync(msg, stoppingToken);
                else
                    await Task.Delay(_options.PollingIntervalMs, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore cancellation
        }
        finally
        {
            _channel.Writer.TryComplete();
        }
    }

    private async Task ConsumeMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var messageQueue = scope.ServiceProvider.GetRequiredService<IMessageQueue>();
        var sessionManager = scope.ServiceProvider.GetRequiredService<ISessionManager>();
        var agentExecutor = scope.ServiceProvider.GetRequiredService<IAgentExecutor>();
        var messageCallback = scope.ServiceProvider.GetRequiredService<IMessageCallback>();

        await foreach (var message in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            _logger.LogDebug("开始处理消息: {MessageId}, 类型: {Type}",
            message.Id, message.Type);

            try
            {
                await ProcessMessageAsync(
                    message,
                    messageQueue,
                    sessionManager,
                    agentExecutor,
                    messageCallback,
                    stoppingToken);

                await messageQueue.CompleteAsync(message.Id, stoppingToken);
                _logger.LogDebug("消息处理完成: {MessageId}", message.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "消息处理失败: {MessageId}", message.Id);
                await HandleMessageFailureAsync(message, messageQueue, ex.Message, stoppingToken);
            }
        }
    }

    private async Task ProcessMessageAsync(
        QueuedMessage queuedMessage,
        IMessageQueue messageQueue,
        ISessionManager sessionManager,
        IAgentExecutor agentExecutor,
        IMessageCallback messageCallback,
        CancellationToken stoppingToken)
    {
        switch (queuedMessage.Type)
        {
            case QueuedMessageType.Incoming:
                await ProcessIncomingMessageAsync(
                    queuedMessage, sessionManager, agentExecutor, messageCallback, stoppingToken);
                break;

            case QueuedMessageType.Outgoing:
                await ProcessOutgoingMessageAsync(queuedMessage, messageCallback, stoppingToken);
                break;

            case QueuedMessageType.Retry:
                // 重试消息按原类型处理
                await ProcessOutgoingMessageAsync(queuedMessage, messageCallback, stoppingToken);
                break;

            default:
                _logger.LogWarning("未知的消息类型: {Type}", queuedMessage.Type);
                break;
        }
    }

    private async Task ProcessIncomingMessageAsync(
        QueuedMessage queuedMessage,
        ISessionManager sessionManager,
        IAgentExecutor agentExecutor,
        IMessageCallback messageCallback,
        CancellationToken stoppingToken)
    {
        // 获取或创建会话
        var session = await sessionManager.GetOrCreateSessionAsync(
            queuedMessage.Message.SenderId,
            queuedMessage.Message.Platform,
            stoppingToken);

        // 添加用户消息到会话历史
        session.AddMessage(queuedMessage.Message);

        // 执行 Agent 处理
        var response = await agentExecutor.ExecuteAsync(
            queuedMessage.Message, session, stoppingToken);

        // Reply target: use ReceiverId (channel) when available, fall back to SenderId (DM)
        var replyTarget = !string.IsNullOrEmpty(queuedMessage.Message.ReceiverId)
            ? queuedMessage.Message.ReceiverId
            : queuedMessage.Message.SenderId;

        if (response.Success && response.Messages.Any())
        {
            // 发送响应消息
            foreach (var responseMessage in response.Messages)
            {
                // Propagate metadata from original message (e.g. thread_ts for Slack threading)
                var messageToSend = responseMessage;
                if (queuedMessage.Message.Metadata != null && responseMessage.Metadata == null)
                {
                    messageToSend = new ChatMessage
                    {
                        MessageId = responseMessage.MessageId,
                        SenderId = responseMessage.SenderId,
                        ReceiverId = responseMessage.ReceiverId,
                        Content = responseMessage.Content,
                        MessageType = responseMessage.MessageType,
                        Platform = responseMessage.Platform,
                        Timestamp = responseMessage.Timestamp,
                        Metadata = new Dictionary<string, object>(queuedMessage.Message.Metadata)
                    };
                }

                var sendResult = await messageCallback.SendAsync(
                    queuedMessage.Message.Platform,
                    replyTarget,
                    messageToSend,
                    stoppingToken);

                if (!sendResult.Success)
                {
                    _logger.LogWarning("响应消息发送失败: {ErrorMessage}", sendResult.ErrorMessage);
                }

                // 添加 Agent 响应到会话历史
                session.AddMessage(responseMessage);
            }
        }
        else if (!response.Success)
        {
            // 发送错误消息给用户
            var errorMessage = new ChatMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                SenderId = "system",
                ReceiverId = replyTarget,
                Content = response.ErrorMessage ?? "An error occurred while processing your message, please try again later.",
                MessageType = ChatMessageType.Text,
                Platform = queuedMessage.Message.Platform,
                Timestamp = DateTimeOffset.UtcNow,
                Metadata = queuedMessage.Message.Metadata != null
                    ? new Dictionary<string, object>(queuedMessage.Message.Metadata)
                    : null
            };

            await messageCallback.SendAsync(
                queuedMessage.Message.Platform,
                replyTarget,
                errorMessage,
                stoppingToken);
        }

        // 更新会话
        await sessionManager.UpdateSessionAsync(session, stoppingToken);
    }

    private async Task ProcessOutgoingMessageAsync(
        QueuedMessage queuedMessage,
        IMessageCallback messageCallback,
        CancellationToken stoppingToken)
    {
        var result = await messageCallback.SendAsync(
            queuedMessage.Message.Platform,
            queuedMessage.TargetUserId,
            queuedMessage.Message,
            stoppingToken);

        if (!result.Success && result.ShouldRetry)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "消息发送失败");
        }
    }

    private async Task HandleMessageFailureAsync(
        QueuedMessage queuedMessage,
        IMessageQueue messageQueue,
        string reason,
        CancellationToken stoppingToken)
    {
        if (queuedMessage.RetryCount < _options.MaxRetryCount)
        {
            // 加入重试队列
            var delaySeconds = CalculateRetryDelay(queuedMessage.RetryCount);
            await messageQueue.RetryAsync(queuedMessage.Id, delaySeconds, stoppingToken);
            _logger.LogInformation("消息已加入重试队列: {MessageId}, 延迟: {Delay}秒",
                queuedMessage.Id, delaySeconds);
        }
        else
        {
            // 移入死信队列
            await messageQueue.FailAsync(queuedMessage.Id, reason, stoppingToken);
            _logger.LogWarning("消息已移入死信队列: {MessageId}, 原因: {Reason}",
                queuedMessage.Id, reason);
        }
    }

    /// <summary>
    /// 计算重试延迟（指数退避）
    /// </summary>
    private int CalculateRetryDelay(int retryCount)
    {
        // 指数退避: 30s, 60s, 120s, ...
        return _options.BaseRetryDelaySeconds * (int)Math.Pow(2, retryCount);
    }
}
