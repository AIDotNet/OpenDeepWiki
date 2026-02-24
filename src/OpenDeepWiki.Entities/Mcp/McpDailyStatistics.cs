namespace OpenDeepWiki.Entities;

/// <summary>
/// MCP 每日统计聚合实体
/// </summary>
public class McpDailyStatistics : AggregateRoot<string>
{
    /// <summary>
    /// 提供商 ID
    /// </summary>
    public string? McpProviderId { get; set; }

    /// <summary>
    /// 统计日期
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// 请求总数
    /// </summary>
    public long RequestCount { get; set; }

    /// <summary>
    /// 成功数
    /// </summary>
    public long SuccessCount { get; set; }

    /// <summary>
    /// 错误数
    /// </summary>
    public long ErrorCount { get; set; }

    /// <summary>
    /// 总耗时（毫秒）
    /// </summary>
    public long TotalDurationMs { get; set; }

    /// <summary>
    /// 输入 Token 总量
    /// </summary>
    public long InputTokens { get; set; }

    /// <summary>
    /// 输出 Token 总量
    /// </summary>
    public long OutputTokens { get; set; }
}
