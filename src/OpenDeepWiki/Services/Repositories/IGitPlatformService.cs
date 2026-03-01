namespace OpenDeepWiki.Services.Repositories;

/// <summary>
/// Git平台仓库统计信息
/// </summary>
public record GitRepoStats(int StarCount, int ForkCount);

/// <summary>
/// Git仓库分支信息
/// </summary>
public record GitBranchInfo(string Name, bool IsDefault);

/// <summary>
/// 获取分支列表的结果
/// </summary>
public record GitBranchesResult(List<GitBranchInfo> Branches, string? DefaultBranch, bool IsSupported);

/// <summary>
/// Git仓库基本信息
/// </summary>
public record GitRepoInfo(
    bool Exists,
    string? Name,
    string? Description,
    string? DefaultBranch,
    int StarCount,
    int ForkCount,
    string? Language,
    string? AvatarUrl,
    bool IsPrivate = false
);

/// <summary>
/// Git平台服务接口
/// </summary>
public interface IGitPlatformService
{
    /// <summary>
    /// 获取仓库统计信息（star数、fork数）
    /// </summary>
    /// <param name="gitUrl">Git仓库地址</param>
    /// <returns>统计信息，获取失败返回null</returns>
    Task<GitRepoStats?> GetRepoStatsAsync(string gitUrl);

    /// <summary>
    /// 获取仓库分支列表
    /// </summary>
    /// <param name="gitUrl">Git仓库地址</param>
    /// <returns>分支列表结果</returns>
    Task<GitBranchesResult> GetBranchesAsync(string gitUrl);

    /// <summary>
    /// 检查仓库是否存在并获取基本信息
    /// </summary>
    /// <param name="owner">仓库所有者</param>
    /// <param name="repo">仓库名称</param>
    /// <returns>仓库信息</returns>
    Task<GitRepoInfo> CheckRepoExistsAsync(string owner, string repo);
}
