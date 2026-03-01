namespace OpenDeepWiki.Services.GitHub;

/// <summary>
/// Information about a GitHub App installation.
/// </summary>
public class GitHubInstallationInfo
{
    public long Id { get; set; }
    public string AccountLogin { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public long AccountId { get; set; }
    public string? AvatarUrl { get; set; }
}

/// <summary>
/// A repository accessible via a GitHub App installation.
/// </summary>
public class GitHubInstallationRepo
{
    public long Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public bool Private { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
    public int StargazersCount { get; set; }
    public int ForksCount { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public string CloneUrl { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
}

/// <summary>
/// Result of listing installation repositories (paginated).
/// </summary>
public class GitHubRepoListResult
{
    public int TotalCount { get; set; }
    public List<GitHubInstallationRepo> Repositories { get; set; } = new();
}

/// <summary>
/// Service for GitHub App operations: JWT generation, installation tokens, repo listing.
/// </summary>
public interface IGitHubAppService
{
    /// <summary>
    /// Whether the GitHub App is configured (App ID and Private Key are set).
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// List all installations of the GitHub App.
    /// </summary>
    Task<List<GitHubInstallationInfo>> ListInstallationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get or refresh an installation access token.
    /// </summary>
    Task<string> GetInstallationTokenAsync(long installationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// List repositories accessible by a specific installation.
    /// </summary>
    Task<GitHubRepoListResult> ListInstallationReposAsync(long installationId, int page = 1, int perPage = 30, CancellationToken cancellationToken = default);
}
