using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Models;

/// <summary>
/// 仓库列表响应
/// </summary>
public class RepositoryListResponse
{
    /// <summary>
    /// 仓库列表
    /// </summary>
    public List<RepositoryItemResponse> Items { get; set; } = [];

    /// <summary>
    /// 总数
    /// </summary>
    public int Total { get; set; }
}

/// <summary>
/// 仓库列表项响应
/// </summary>
public class RepositoryItemResponse
{
    /// <summary>
    /// 仓库ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 组织名称
    /// </summary>
    public string OrgName { get; set; } = string.Empty;

    /// <summary>
    /// 仓库名称
    /// </summary>
    public string RepoName { get; set; } = string.Empty;

    /// <summary>
    /// Git URL
    /// </summary>
    public string GitUrl { get; set; } = string.Empty;

    /// <summary>
    /// 仓库来源类型
    /// </summary>
    public RepositorySourceType SourceType { get; set; } = RepositorySourceType.Git;

    /// <summary>
    /// 仓库来源类型名称
    /// </summary>
    public string SourceTypeName => SourceType.ToString();

    /// <summary>
    /// 对外展示的来源信息
    /// </summary>
    public string SourceLocation { get; set; } = string.Empty;

    /// <summary>
    /// 处理状态
    /// </summary>
    public RepositoryStatus Status { get; set; }

    /// <summary>
    /// 状态名称
    /// </summary>
    public string StatusName => Status.ToString();

    /// <summary>
    /// 是否公开
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// 是否设置了密码
    /// </summary>
    public bool HasPassword { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Star数量
    /// </summary>
    public int StarCount { get; set; }

    /// <summary>
    /// Fork数量
    /// </summary>
    public int ForkCount { get; set; }

    /// <summary>
    /// 主要编程语言
    /// </summary>
    public string? PrimaryLanguage { get; set; }
}
