using System.ComponentModel.DataAnnotations;

namespace OpenDeepWiki.Entities;

/// <summary>
/// MCP 提供商配置实体（管理员管理）
/// </summary>
public class McpProvider : AggregateRoot<string>
{
    /// <summary>
    /// 提供商名称
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 提供商描述
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// MCP 服务端点地址
    /// </summary>
    [Required]
    [StringLength(500)]
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// 传输方式：sse | streamable_http
    /// </summary>
    [Required]
    [StringLength(50)]
    public string TransportType { get; set; } = "streamable_http";

    /// <summary>
    /// 是否需要用户提供 API Key
    /// </summary>
    public bool RequiresApiKey { get; set; } = true;

    /// <summary>
    /// 用户获取 API Key 的地址（管理员填写）
    /// </summary>
    [StringLength(500)]
    public string? ApiKeyObtainUrl { get; set; }

    /// <summary>
    /// 系统级 API Key（RequiresApiKey=false 时系统自动带上）
    /// </summary>
    [StringLength(500)]
    public string? SystemApiKey { get; set; }

    /// <summary>
    /// 关联的 AI 模型配置 ID（FK → ModelConfig）
    /// </summary>
    [StringLength(100)]
    public string? ModelConfigId { get; set; }

    /// <summary>
    /// 管理员配置的请求类型 JSON 数组
    /// </summary>
    public string? RequestTypes { get; set; }

    /// <summary>
    /// 允许暴露的工具 JSON 数组
    /// </summary>
    public string? AllowedTools { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 排序
    /// </summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>
    /// 提供商图标 URL
    /// </summary>
    [StringLength(500)]
    public string? IconUrl { get; set; }

    /// <summary>
    /// 每日请求限额（0=无限制）
    /// </summary>
    public int MaxRequestsPerDay { get; set; } = 0;
}
