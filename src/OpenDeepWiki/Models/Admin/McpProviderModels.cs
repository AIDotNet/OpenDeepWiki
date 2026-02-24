namespace OpenDeepWiki.Models.Admin;

/// <summary>
/// MCP 提供商创建/更新请求
/// </summary>
public class McpProviderRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public string TransportType { get; set; } = "streamable_http";
    public bool RequiresApiKey { get; set; } = true;
    public string? ApiKeyObtainUrl { get; set; }
    public string? SystemApiKey { get; set; }
    public string? ModelConfigId { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public string? IconUrl { get; set; }
    public int MaxRequestsPerDay { get; set; }
}

/// <summary>
/// MCP 提供商 DTO
/// </summary>
public class McpProviderDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public string TransportType { get; set; } = "streamable_http";
    public bool RequiresApiKey { get; set; }
    public string? ApiKeyObtainUrl { get; set; }
    public bool HasSystemApiKey { get; set; }
    public string? ModelConfigId { get; set; }
    public string? ModelConfigName { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public string? IconUrl { get; set; }
    public int MaxRequestsPerDay { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// MCP 使用日志 DTO
/// </summary>
public class McpUsageLogDto
{
    public string Id { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? McpProviderId { get; set; }
    public string? McpProviderName { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string? RequestSummary { get; set; }
    public int ResponseStatus { get; set; }
    public long DurationMs { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string? IpAddress { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// MCP 使用日志查询过滤器
/// </summary>
public class McpUsageLogFilter
{
    public string? McpProviderId { get; set; }
    public string? UserId { get; set; }
    public string? ToolName { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// 分页结果
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>
/// MCP 使用统计响应
/// </summary>
public class McpUsageStatisticsResponse
{
    public List<McpDailyUsage> DailyUsages { get; set; } = new();
    public long TotalRequests { get; set; }
    public long TotalSuccessful { get; set; }
    public long TotalErrors { get; set; }
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
}

/// <summary>
/// MCP 每日使用量
/// </summary>
public class McpDailyUsage
{
    public DateTime Date { get; set; }
    public long RequestCount { get; set; }
    public long SuccessCount { get; set; }
    public long ErrorCount { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
}
