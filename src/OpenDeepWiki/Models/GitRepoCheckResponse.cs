namespace OpenDeepWiki.Models;

/// <summary>
/// GitHub仓库检查响应
/// </summary>
public class GitRepoCheckResponse
{
    /// <summary>
    /// 仓库是否存在
    /// </summary>
    public bool Exists { get; set; }

    /// <summary>
    /// 仓库名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 仓库描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 默认分支
    /// </summary>
    public string? DefaultBranch { get; set; }

    /// <summary>
    /// Star数量
    /// </summary>
    public int StarCount { get; set; }

    /// <summary>
    /// Fork数量
    /// </summary>
    public int ForkCount { get; set; }

    /// <summary>
    /// 主要语言
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// 头像URL
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// 是否为私有仓库
    /// </summary>
    public bool IsPrivate { get; set; }

    /// <summary>
    /// Git仓库地址
    /// </summary>
    public string? GitUrl { get; set; }
}
