namespace OpenDeepWiki.Models;

/// <summary>
/// GitHub repository check response
/// </summary>
public class GitRepoCheckResponse
{
    /// <summary>
    /// Whether the repository exists
    /// </summary>
    public bool Exists { get; set; }

    /// <summary>
    /// Repository name
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Repository description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Default branch
    /// </summary>
    public string? DefaultBranch { get; set; }

    /// <summary>
    /// Star count
    /// </summary>
    public int StarCount { get; set; }

    /// <summary>
    /// Fork count
    /// </summary>
    public int ForkCount { get; set; }

    /// <summary>
    /// Primary language
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Avatar URL
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Whether the repository is private
    /// </summary>
    public bool IsPrivate { get; set; }

    /// <summary>
    /// Git repository URL
    /// </summary>
    public string? GitUrl { get; set; }
}
