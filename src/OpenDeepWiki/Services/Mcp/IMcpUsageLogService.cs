using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Mcp;

/// <summary>
/// MCP 使用日志服务接口
/// </summary>
public interface IMcpUsageLogService
{
    /// <summary>
    /// 异步记录 MCP 使用日志（不阻塞请求）
    /// </summary>
    Task LogUsageAsync(McpUsageLog log);

    /// <summary>
    /// 聚合指定日期的日志到每日统计
    /// </summary>
    Task AggregateDailyStatisticsAsync(DateTime date);
}
