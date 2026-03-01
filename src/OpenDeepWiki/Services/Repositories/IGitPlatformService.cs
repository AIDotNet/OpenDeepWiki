namespace OpenDeepWiki.Services.Repositories;

/// <summary>
/// Git platform repository statistics
/// </summary>
public record GitRepoStats(int StarCount, int ForkCount);

/// <summary>
/// Git repository branch information
/// </summary>
public record GitBranchInfo(string Name, bool IsDefault);

/// <summary>
/// Result of getting branch list
/// </summary>
public record GitBranchesResult(List<GitBranchInfo> Branches, string? DefaultBranch, bool IsSupported);

/// <summary>
/// Git repository basic information
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
/// Git platform service interface
/// </summary>
public interface IGitPlatformService
{
    /// <summary>
    /// Get repository statistics (star count, fork count)
    /// </summary>
    /// <param name="gitUrl">Git repository URL</param>
    /// <returns>Statistics, returns null on failure</returns>
    Task<GitRepoStats?> GetRepoStatsAsync(string gitUrl);

    /// <summary>
    /// Get repository branch list
    /// </summary>
    /// <param name="gitUrl">Git repository URL</param>
    /// <returns>Branch list result</returns>
    Task<GitBranchesResult> GetBranchesAsync(string gitUrl);

    /// <summary>
    /// Check if repository exists and get basic information
    /// </summary>
    /// <param name="owner">Repository owner</param>
    /// <param name="repo">Repository name</param>
    /// <returns>Repository information</returns>
    Task<GitRepoInfo> CheckRepoExistsAsync(string owner, string repo);
}
