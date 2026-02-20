using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenDeepWiki.Chat.Routing;

namespace OpenDeepWiki.Services.Chat;

[MiniApi(Route = "/api/chat/webhook")]
[Tags("Chat Webhook")]
public class ChatWebhookApiService(IMessageRouter messageRouter, ILogger<ChatWebhookApiService> logger)
{
    /// <summary>
    /// 飞书 Webhook
    /// </summary>
    [HttpPost("/feishu")]
    [AllowAnonymous]
    public Task<IResult> HandleFeishuWebhookAsync(HttpContext httpContext)
    {
        return HandlePlatformWebhookAsync("feishu", httpContext);
    }

    /// <summary>
    /// QQ 机器人 Webhook
    /// </summary>
    [HttpPost("/qq")]
    [AllowAnonymous]
    public Task<IResult> HandleQqWebhookAsync(HttpContext httpContext)
    {
        return HandlePlatformWebhookAsync("qq", httpContext);
    }

    /// <summary>
    /// 微信 Webhook 验证
    /// </summary>
    [HttpGet("/wechat")]
    [AllowAnonymous]
    public async Task<IResult> HandleWeChatVerificationAsync(HttpContext httpContext)
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
            return Results.Text(validationResult.Challenge);
        }

        return Results.BadRequest(new { error = validationResult.ErrorMessage ?? "Validation failed" });
    }

    /// <summary>
    /// 微信客服消息 Webhook
    /// </summary>
    [HttpPost("/wechat")]
    [AllowAnonymous]
    public Task<IResult> HandleWeChatWebhookAsync(HttpContext httpContext)
    {
        return HandlePlatformWebhookAsync("wechat", httpContext);
    }

    /// <summary>
    /// Slack Webhook
    /// </summary>
    [HttpPost("/slack")]
    [AllowAnonymous]
    public Task<IResult> HandleSlackWebhookAsync(HttpContext httpContext)
    {
        return HandlePlatformWebhookAsync("slack", httpContext);
    }

    /// <summary>
    /// 通用 Webhook
    /// </summary>
    [HttpPost("/{platform}")]
    [AllowAnonymous]
    public Task<IResult> HandleGenericWebhookAsync(string platform, HttpContext httpContext)
    {
        return HandlePlatformWebhookAsync(platform, httpContext);
    }

    private async Task<IResult> HandlePlatformWebhookAsync(string platform, HttpContext httpContext)
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

            var validationResult = await provider.ValidateWebhookAsync(httpContext.Request);

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

            httpContext.Request.EnableBuffering();
            httpContext.Request.Body.Position = 0;
            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
            var rawMessage = await reader.ReadToEndAsync();

            var message = await provider.ParseMessageAsync(rawMessage);

            if (message == null)
            {
                logger.LogDebug("No message parsed from {Platform} webhook, possibly non-message event", platform);
                return Results.Ok(new { status = "ok" });
            }

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
            return Results.Json(new { error = "Internal server error" }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
