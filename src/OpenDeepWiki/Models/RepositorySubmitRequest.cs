using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Models;

/// <summary>
/// 仓库提交请求
/// </summary>
public class RepositorySubmitRequest
{
    /// <summary>
    /// Git地址
    /// </summary>
    [Required]
    [StringLength(500)]
    public string GitUrl { get; set; } = string.Empty;

    /// <summary>
    /// 仓库名称
    /// </summary>
    [Required]
    [StringLength(100)]
    public string RepoName { get; set; } = string.Empty;

    /// <summary>
    /// 仓库组织
    /// </summary>
    [Required]
    [StringLength(100)]
    public string OrgName { get; set; } = string.Empty;

    /// <summary>
    /// 仓库账户
    /// </summary>
    [StringLength(200)]
    public string? AuthAccount { get; set; }

    /// <summary>
    /// 仓库密码（明文存储）
    /// </summary>
    [StringLength(500)]
    public string? AuthPassword { get; set; }

    /// <summary>
    /// 仓库分支
    /// </summary>
    [Required]
    [StringLength(200)]
    public string BranchName { get; set; } = string.Empty;

    /// <summary>
    /// 仓库当前生成语言
    /// </summary>
    [Required]
    [StringLength(50)]
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// 是否公开
    /// </summary>
    public bool IsPublic { get; set; } = true;
}

/// <summary>
/// 压缩包仓库提交请求
/// </summary>
public class ArchiveRepositorySubmitRequest
{
    /// <summary>
    /// 仓库组织
    /// </summary>
    [Required]
    [StringLength(100)]
    public string OrgName { get; set; } = string.Empty;

    /// <summary>
    /// 仓库名称
    /// </summary>
    [Required]
    [StringLength(100)]
    public string RepoName { get; set; } = string.Empty;

    /// <summary>
    /// 分支名称
    /// </summary>
    [StringLength(200)]
    public string BranchName { get; set; } = "main";

    /// <summary>
    /// 文档语言
    /// </summary>
    [Required]
    [StringLength(50)]
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// 是否公开
    /// </summary>
    public bool IsPublic { get; set; } = false;

    /// <summary>
    /// 上传的 ZIP 压缩包
    /// </summary>
    [Required]
    public IFormFile? Archive { get; set; }
}

/// <summary>
/// 本地目录仓库提交请求
/// </summary>
public class LocalDirectoryRepositorySubmitRequest
{
    /// <summary>
    /// 仓库组织
    /// </summary>
    [Required]
    [StringLength(100)]
    public string OrgName { get; set; } = string.Empty;

    /// <summary>
    /// 仓库名称
    /// </summary>
    [Required]
    [StringLength(100)]
    public string RepoName { get; set; } = string.Empty;

    /// <summary>
    /// 服务器本地目录
    /// </summary>
    [Required]
    [StringLength(500)]
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>
    /// 分支名称
    /// </summary>
    [StringLength(200)]
    public string BranchName { get; set; } = "main";

    /// <summary>
    /// 文档语言
    /// </summary>
    [Required]
    [StringLength(50)]
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// 是否公开
    /// </summary>
    public bool IsPublic { get; set; } = false;
}
