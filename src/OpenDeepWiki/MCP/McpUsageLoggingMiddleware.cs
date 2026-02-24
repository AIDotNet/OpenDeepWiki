using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Mcp;

namespace OpenDeepWiki.MCP;

/// <summary>
/// MCP 请求使用日志中间件
/// 拦截 /api/mcp 路径的请求，记录工具调用、耗时、状态码
/// </summary>
public class McpUsageLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _path;

    public McpUsageLoggingMiddleware(RequestDelegate next, string path)
    {
        _next = next;
        _path = path;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(_path))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var originalStatusCode = context.Response.StatusCode;

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            // Fire-and-forget logging
            var logService = context.RequestServices.GetService<IMcpUsageLogService>();
            if (logService != null)
            {
                var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? context.User?.FindFirstValue("sub");
                var ipAddress = context.Connection.RemoteIpAddress?.ToString();
                var userAgent = context.Request.Headers.UserAgent.FirstOrDefault();

                // Try to extract tool name from request body (MCP JSON-RPC)
                var toolName = ExtractToolName(context);

                var log = new McpUsageLog
                {
                    UserId = userId,
                    ToolName = toolName ?? "unknown",
                    ResponseStatus = context.Response.StatusCode,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    IpAddress = ipAddress,
                    UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent
                };

                // Don't await - fire and forget
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await logService.LogUsageAsync(log);
                    }
                    catch
                    {
                        // Silently ignore logging failures
                    }
                });
            }
        }
    }

    /// <summary>
    /// 尝试从 MCP JSON-RPC 请求中提取工具名称
    /// </summary>
    private static string? ExtractToolName(HttpContext context)
    {
        // MCP uses JSON-RPC, tool calls have method "tools/call" with params.name
        // We store the tool name in HttpContext.Items during request processing if available
        if (context.Items.TryGetValue("McpToolName", out var toolNameObj) && toolNameObj is string toolName)
        {
            return toolName;
        }

        // Fallback: use the request method + path segment
        return context.Request.Method + " " + context.Request.Path.Value;
    }
}

public static class McpUsageLoggingExtensions
{
    public static IApplicationBuilder UseMcpUsageLogging(this IApplicationBuilder app, string path)
    {
        return app.UseMiddleware<McpUsageLoggingMiddleware>(path);
    }
}
