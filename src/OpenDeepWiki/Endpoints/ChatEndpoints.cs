using System.Text;
using Microsoft.AspNetCore.Mvc;
using OpenDeepWiki.Chat.Config;
using OpenDeepWiki.Chat.Queue;
using OpenDeepWiki.Chat.Routing;

namespace OpenDeepWiki.Endpoints;

/// <summary>
/// Chat 端点日志类（用于泛型日志记录器）
/// </summary>
public class ChatEndpointsLogger { }

/// <summary>
/// Chat 系统端点
/// 包含 Webhook 接收端点和管理端点
/// </summary>
public static class ChatEndpoints
{
    /// <summary>
    /// 注册所有 Chat 相关端点
    /// </summary>
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        // Webhook 端点组
        var webhookGroup = app.MapGroup("/api/chat/webhook")
            .WithTags("Chat Webhook");
        
        webhookGroup.MapWebhookEndpoints();
        
        // 管理端点组
        var adminGroup = app.MapGroup("/api/chat/admin")
            .WithTags("Chat 管理");
        
        adminGroup.MapChatAdminEndpoints();
        
        return app;
    }
    
    /// <summary>
    /// 注册 Webhook 端点
    /// </summary>
    private static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        // 飞书 Webhook
        app.MapPost("/feishu", HandleFeishuWebhookAsync)
            .WithName("FeishuWebhook")
            .WithSummary("飞书消息 Webhook")
            .AllowAnonymous();
        
        // QQ 机器人 Webhook
        app.MapPost("/qq", HandleQQWebhookAsync)
            .WithName("QQWebhook")
            .WithSummary("QQ机器人消息 Webhook")
            .AllowAnonymous();
        
        // 微信客服 Webhook (GET 用于验证，POST 用于接收消息)
        app.MapGet("/wechat", HandleWeChatVerificationAsync)
            .WithName("WeChatVerification")
            .WithSummary("微信 Webhook 验证")
            .AllowAnonymous();
        
        app.MapPost("/wechat", HandleWeChatWebhookAsync)
            .WithName("WeChatWebhook")
            .WithSummary("微信客服消息 Webhook")
            .AllowAnonymous();
        
        // Slack Webhook
        app.MapPost("/slack", HandleSlackWebhookAsync)
            .WithName("SlackWebhook")
            .WithSummary("Slack message Webhook")
            .AllowAnonymous();

        // 通用 Webhook（根据平台参数路由）
        app.MapPost("/{platform}", HandleGenericWebhookAsync)
            .WithName("GenericWebhook")
            .WithSummary("通用消息 Webhook")
            .AllowAnonymous();
        
        return app;
    }
    
    /// <summary>
    /// 注册管理端点
    /// </summary>
    private static IEndpointRouteBuilder MapChatAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // Provider 配置管理
        app.MapGet("/providers", GetAllProvidersAsync)
            .WithName("GetAllProviders")
            .WithSummary("获取所有 Provider 配置");
        
        app.MapGet("/providers/{platform}", GetProviderConfigAsync)
            .WithName("GetProviderConfig")
            .WithSummary("获取指定 Provider 配置");
        
        app.MapPost("/providers", SaveProviderConfigAsync)
            .WithName("SaveProviderConfig")
            .WithSummary("保存 Provider 配置");
        
        app.MapDelete("/providers/{platform}", DeleteProviderConfigAsync)
            .WithName("DeleteProviderConfig")
            .WithSummary("删除 Provider 配置");
        
        app.MapPost("/providers/{platform}/enable", EnableProviderAsync)
            .WithName("EnableProvider")
            .WithSummary("启用 Provider");
        
        app.MapPost("/providers/{platform}/disable", DisableProviderAsync)
            .WithName("DisableProvider")
            .WithSummary("禁用 Provider");
        
        app.MapPost("/providers/{platform}/reload", ReloadProviderConfigAsync)
            .WithName("ReloadProviderConfig")
            .WithSummary("重载 Provider 配置");
        
        // 队列状态监控
        app.MapGet("/queue/status", GetQueueStatusAsync)
            .WithName("GetQueueStatus")
            .WithSummary("获取队列状态");
        
        app.MapGet("/queue/deadletter", GetDeadLetterMessagesAsync)
            .WithName("GetDeadLetterMessages")
            .WithSummary("获取死信队列消息");
        
        app.MapPost("/queue/deadletter/{messageId}/reprocess", ReprocessDeadLetterAsync)
            .WithName("ReprocessDeadLetter")
            .WithSummary("重新处理死信消息");
        
        app.MapDelete("/queue/deadletter/{messageId}", DeleteDeadLetterAsync)
            .WithName("DeleteDeadLetter")
            .WithSummary("删除死信消息");
        
        app.MapDelete("/queue/deadletter", ClearDeadLetterQueueAsync)
            .WithName("ClearDeadLetterQueue")
            .WithSummary("清空死信队列");
        
        return app;
    }

    
    #region Webhook 处理方法
    
    /// <summary>
    /// 处理飞书 Webhook
    /// </summary>
    private static async Task<IResult> HandleFeishuWebhookAsync(
        HttpContext httpContext,
        [FromServices] IMessageRouter messageRouter,
        [FromServices] ILogger<ChatEndpointsLogger> logger)
    {
        return await HandlePlatformWebhookAsync("feishu", httpContext, messageRouter, logger);
    }
    
    /// <summary>
    /// 处理 QQ 机器人 Webhook
    /// </summary>
    private static async Task<IResult> HandleQQWebhookAsync(
        HttpContext httpContext,
        [FromServices] IMessageRouter messageRouter,
        [FromServices] ILogger<ChatEndpointsLogger> logger)
    {
        return await HandlePlatformWebhookAsync("qq", httpContext, messageRouter, logger);
    }
    
    /// <summary>
    /// 处理微信验证请求 (GET)
    /// </summary>
    private static async Task<IResult> HandleWeChatVerificationAsync(
        HttpContext httpContext,
        [FromServices] IMessageRouter messageRouter,
        [FromServices] ILogger<ChatEndpointsLogger> logger)
    {
        var provider = messageRouter.GetProvider("wechat");
        if (provider == null)
        {
            logger.LogWarning("WeChat provider not registered");
            return Results.NotFound(new { error = "Provider not found" });
        }
        
        var validationResult = await provider.ValidateWebhookAsync(httpContext.Request);
        
        if (validationResult.IsValid && !string.IsNullOrEmpty(validationResult.Challenge))
        {
            // 微信验证需要直接返回 echostr
            return Results.Text(validationResult.Challenge);
        }
        
        return Results.BadRequest(new { error = validationResult.ErrorMessage ?? "Validation failed" });
    }
    
    /// <summary>
    /// 处理微信消息 Webhook (POST)
    /// </summary>
    private static async Task<IResult> HandleWeChatWebhookAsync(
        HttpContext httpContext,
        [FromServices] IMessageRouter messageRouter,
        [FromServices] ILogger<ChatEndpointsLogger> logger)
    {
        return await HandlePlatformWebhookAsync("wechat", httpContext, messageRouter, logger);
    }
    
    /// <summary>
    /// Handle Slack Webhook
    /// </summary>
    private static async Task<IResult> HandleSlackWebhookAsync(
        HttpContext httpContext,
        [FromServices] IMessageRouter messageRouter,
        [FromServices] ILogger<ChatEndpointsLogger> logger)
    {
        return await HandlePlatformWebhookAsync("slack", httpContext, messageRouter, logger);
    }

    /// <summary>
    /// 处理通用 Webhook
    /// </summary>
    private static async Task<IResult> HandleGenericWebhookAsync(
        string platform,
        HttpContext httpContext,
        [FromServices] IMessageRouter messageRouter,
        [FromServices] ILogger<ChatEndpointsLogger> logger)
    {
        return await HandlePlatformWebhookAsync(platform, httpContext, messageRouter, logger);
    }
    
    /// <summary>
    /// 统一的平台 Webhook 处理逻辑
    /// </summary>
    private static async Task<IResult> HandlePlatformWebhookAsync(
        string platform,
        HttpContext httpContext,
        IMessageRouter messageRouter,
        ILogger logger)
    {
        try
        {
            var provider = messageRouter.GetProvider(platform);
            if (provider == null)
            {
                logger.LogWarning("Provider not registered for platform: {Platform}", platform);
                return Results.NotFound(new { error = $"Provider not found for platform: {platform}" });
            }
            
            if (!provider.IsEnabled)
            {
                logger.LogWarning("Provider {Platform} is disabled", platform);
                return Results.BadRequest(new { error = $"Provider {platform} is disabled" });
            }
            
            // 验证 Webhook 请求
            var validationResult = await provider.ValidateWebhookAsync(httpContext.Request);
            
            // 处理验证请求（如飞书的 url_verification）
            if (validationResult.IsValid && !string.IsNullOrEmpty(validationResult.Challenge))
            {
                logger.LogDebug("Webhook validation challenge for {Platform}", platform);
                return Results.Json(new { challenge = validationResult.Challenge });
            }
            
            if (!validationResult.IsValid)
            {
                logger.LogWarning("Webhook validation failed for {Platform}: {Error}", 
                    platform, validationResult.ErrorMessage);
                return Results.BadRequest(new { error = validationResult.ErrorMessage ?? "Validation failed" });
            }
            
            // 读取请求体
            httpContext.Request.EnableBuffering();
            httpContext.Request.Body.Position = 0;
            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
            var rawMessage = await reader.ReadToEndAsync();
            
            // 解析消息
            var message = await provider.ParseMessageAsync(rawMessage);
            
            if (message == null)
            {
                // 可能是非消息事件，返回成功
                logger.LogDebug("No message parsed from {Platform} webhook, possibly non-message event", platform);
                return Results.Ok(new { status = "ok" });
            }
            
            // 路由消息（异步处理，立即返回确认）
            _ = Task.Run(async () =>
            {
                try
                {
                    await messageRouter.RouteIncomingAsync(message);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to route message {MessageId} from {Platform}", 
                        message.MessageId, platform);
                }
            });
            
            logger.LogDebug("Message {MessageId} from {Platform} accepted for processing", 
                message.MessageId, platform);
            
            return Results.Ok(new { status = "ok", messageId = message.MessageId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling webhook for platform: {Platform}", platform);
            return Results.Json(
                new { error = "Internal server error" }, 
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
    
    #endregion

    
    #region 管理端点处理方法
    
    /// <summary>
    /// 获取所有 Provider 配置
    /// </summary>
    private static async Task<IResult> GetAllProvidersAsync(
        [FromServices] IChatConfigService configService,
        [FromServices] IMessageRouter messageRouter)
    {
        var configs = await configService.GetAllConfigsAsync();
        var providers = messageRouter.GetAllProviders();
        
        var result = configs.Select(c => new ProviderStatusResponse
        {
            Platform = c.Platform,
            DisplayName = c.DisplayName,
            IsEnabled = c.IsEnabled,
            IsRegistered = providers.Any(p => p.PlatformId == c.Platform),
            WebhookUrl = c.WebhookUrl,
            MessageInterval = c.MessageInterval,
            MaxRetryCount = c.MaxRetryCount
        });
        
        return Results.Ok(result);
    }
    
    /// <summary>
    /// 获取指定 Provider 配置
    /// </summary>
    private static async Task<IResult> GetProviderConfigAsync(
        string platform,
        [FromServices] IChatConfigService configService,
        [FromServices] IMessageRouter messageRouter)
    {
        var config = await configService.GetConfigAsync(platform);
        if (config == null)
        {
            return Results.NotFound(new { error = $"Provider config not found for platform: {platform}" });
        }
        
        var provider = messageRouter.GetProvider(platform);
        
        return Results.Ok(new ProviderStatusResponse
        {
            Platform = config.Platform,
            DisplayName = config.DisplayName,
            IsEnabled = config.IsEnabled,
            IsRegistered = provider != null,
            WebhookUrl = config.WebhookUrl,
            MessageInterval = config.MessageInterval,
            MaxRetryCount = config.MaxRetryCount,
            ConfigData = config.ConfigData // 注意：敏感信息应该脱敏
        });
    }
    
    /// <summary>
    /// 保存 Provider 配置
    /// </summary>
    private static async Task<IResult> SaveProviderConfigAsync(
        [FromBody] ProviderConfigDto config,
        [FromServices] IChatConfigService configService)
    {
        // 验证配置
        var validationResult = configService.ValidateConfig(config);
        if (!validationResult.IsValid)
        {
            return Results.BadRequest(new 
            { 
                error = "Configuration validation failed",
                errors = validationResult.Errors,
                missingFields = validationResult.MissingFields
            });
        }
        
        await configService.SaveConfigAsync(config);
        
        return Results.Ok(new { status = "ok", platform = config.Platform });
    }
    
    /// <summary>
    /// 删除 Provider 配置
    /// </summary>
    private static async Task<IResult> DeleteProviderConfigAsync(
        string platform,
        [FromServices] IChatConfigService configService)
    {
        var config = await configService.GetConfigAsync(platform);
        if (config == null)
        {
            return Results.NotFound(new { error = $"Provider config not found for platform: {platform}" });
        }
        
        await configService.DeleteConfigAsync(platform);
        
        return Results.Ok(new { status = "ok", platform });
    }
    
    /// <summary>
    /// 启用 Provider
    /// </summary>
    private static async Task<IResult> EnableProviderAsync(
        string platform,
        [FromServices] IChatConfigService configService)
    {
        var config = await configService.GetConfigAsync(platform);
        if (config == null)
        {
            return Results.NotFound(new { error = $"Provider config not found for platform: {platform}" });
        }
        
        config.IsEnabled = true;
        await configService.SaveConfigAsync(config);
        
        // 触发配置重载
        await configService.ReloadConfigAsync(platform);
        
        return Results.Ok(new { status = "ok", platform, isEnabled = true });
    }
    
    /// <summary>
    /// 禁用 Provider
    /// </summary>
    private static async Task<IResult> DisableProviderAsync(
        string platform,
        [FromServices] IChatConfigService configService)
    {
        var config = await configService.GetConfigAsync(platform);
        if (config == null)
        {
            return Results.NotFound(new { error = $"Provider config not found for platform: {platform}" });
        }
        
        config.IsEnabled = false;
        await configService.SaveConfigAsync(config);
        
        // 触发配置重载
        await configService.ReloadConfigAsync(platform);
        
        return Results.Ok(new { status = "ok", platform, isEnabled = false });
    }
    
    /// <summary>
    /// 重载 Provider 配置
    /// </summary>
    private static async Task<IResult> ReloadProviderConfigAsync(
        string platform,
        [FromServices] IChatConfigService configService)
    {
        var config = await configService.GetConfigAsync(platform);
        if (config == null)
        {
            return Results.NotFound(new { error = $"Provider config not found for platform: {platform}" });
        }
        
        await configService.ReloadConfigAsync(platform);
        
        return Results.Ok(new { status = "ok", platform, message = "Configuration reloaded" });
    }
    
    #endregion

    
    #region 队列监控端点处理方法
    
    /// <summary>
    /// 获取队列状态
    /// </summary>
    private static async Task<IResult> GetQueueStatusAsync(
        [FromServices] IMessageQueue messageQueue)
    {
        var queueLength = await messageQueue.GetQueueLengthAsync();
        var deadLetterCount = await messageQueue.GetDeadLetterCountAsync();
        
        return Results.Ok(new QueueStatusResponse
        {
            PendingCount = queueLength,
            DeadLetterCount = deadLetterCount,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
    
    /// <summary>
    /// 获取死信队列消息
    /// </summary>
    private static async Task<IResult> GetDeadLetterMessagesAsync(
        [FromQuery] int skip,
        [FromQuery] int take,
        [FromServices] IMessageQueue messageQueue)
    {
        if (take <= 0) take = 20;
        if (take > 100) take = 100;
        if (skip < 0) skip = 0;
        
        var messages = await messageQueue.GetDeadLetterMessagesAsync(skip, take);
        var total = await messageQueue.GetDeadLetterCountAsync();
        
        return Results.Ok(new DeadLetterListResponse
        {
            Messages = messages.Select(m => new DeadLetterMessageResponse
            {
                MessageId = m.Id,
                Platform = m.Message.Platform,
                TargetUserId = m.TargetUserId,
                Content = m.Message.Content,
                FailureReason = m.ErrorMessage ?? string.Empty,
                RetryCount = m.RetryCount,
                CreatedAt = m.CreatedAt,
                FailedAt = m.FailedAt
            }).ToList(),
            Total = total,
            Skip = skip,
            Take = take
        });
    }
    
    /// <summary>
    /// 重新处理死信消息
    /// </summary>
    private static async Task<IResult> ReprocessDeadLetterAsync(
        string messageId,
        [FromServices] IMessageQueue messageQueue,
        [FromServices] ILogger<ChatEndpointsLogger> logger)
    {
        var success = await messageQueue.ReprocessDeadLetterAsync(messageId);
        
        if (!success)
        {
            return Results.NotFound(new { error = $"Dead letter message not found: {messageId}" });
        }
        
        logger.LogInformation("Dead letter message {MessageId} requeued for processing", messageId);
        
        return Results.Ok(new { status = "ok", messageId, message = "Message requeued for processing" });
    }
    
    /// <summary>
    /// 删除死信消息
    /// </summary>
    private static async Task<IResult> DeleteDeadLetterAsync(
        string messageId,
        [FromServices] IMessageQueue messageQueue,
        [FromServices] ILogger<ChatEndpointsLogger> logger)
    {
        var success = await messageQueue.DeleteDeadLetterAsync(messageId);
        
        if (!success)
        {
            return Results.NotFound(new { error = $"Dead letter message not found: {messageId}" });
        }
        
        logger.LogInformation("Dead letter message {MessageId} deleted", messageId);
        
        return Results.Ok(new { status = "ok", messageId, message = "Message deleted" });
    }
    
    /// <summary>
    /// 清空死信队列
    /// </summary>
    private static async Task<IResult> ClearDeadLetterQueueAsync(
        [FromServices] IMessageQueue messageQueue,
        [FromServices] ILogger<ChatEndpointsLogger> logger)
    {
        var count = await messageQueue.ClearDeadLetterQueueAsync();
        
        logger.LogInformation("Dead letter queue cleared, {Count} messages deleted", count);
        
        return Results.Ok(new { status = "ok", deletedCount = count });
    }
    
    #endregion
}

#region 响应模型

/// <summary>
/// Provider 状态响应
/// </summary>
public class ProviderStatusResponse
{
    public string Platform { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsRegistered { get; set; }
    public string? WebhookUrl { get; set; }
    public int MessageInterval { get; set; }
    public int MaxRetryCount { get; set; }
    public string? ConfigData { get; set; }
}

/// <summary>
/// 队列状态响应
/// </summary>
public class QueueStatusResponse
{
    public int PendingCount { get; set; }
    public int DeadLetterCount { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>
/// 死信消息列表响应
/// </summary>
public class DeadLetterListResponse
{
    public List<DeadLetterMessageResponse> Messages { get; set; } = new();
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

/// <summary>
/// 死信消息响应
/// </summary>
public class DeadLetterMessageResponse
{
    public string MessageId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string TargetUserId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset FailedAt { get; set; }
}

#endregion
