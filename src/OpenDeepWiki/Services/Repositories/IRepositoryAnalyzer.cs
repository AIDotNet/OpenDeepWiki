using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Repositories;

/// <summary>
/// Interface for repository analysis operations.
/// Handles cloning, updating, and analyzing Git repositories.
/// </summary>
public interface IRepositoryAnalyzer
{
    /// <summary>
    /// Clones or updates a repository to a local working directory.
    /// </summary>
    /// <param name="repository">The repository entity to process.</param>
    /// <param name="branchName">The branch name to checkout.</param>
    /// <param name="previousCommitId">The previous commit ID for incremental updates (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A workspace containing the repository information and paths.</returns>
    Task<RepositoryWorkspace> PrepareWorkspaceAsync(
        Repository repository,
        string branchName,
        string? previousCommitId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up the working directory after processing.
    /// </summary>
    /// <param name="workspace">The workspace to clean up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CleanupWorkspaceAsync(RepositoryWorkspace workspace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of files changed between two commits.
    /// </summary>
    /// <param name="workspace">The repository workspace.</param>
    /// <param name="fromCommitId">The starting commit ID (null for initial processing).</param>
    /// <param name="toCommitId">The ending commit ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of relative file paths that changed.</returns>
    Task<string[]> GetChangedFilesAsync(
        RepositoryWorkspace workspace,
        string? fromCommitId,
        string toCommitId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects the primary programming language of the repository.
    /// </summary>
    /// <param name="workspace">The repository workspace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The detected primary language name, or null if unable to detect.</returns>
    Task<string?> DetectPrimaryLanguageAsync(
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a prepared repository workspace for AI processing.
/// </summary>
public class RepositoryWorkspace
{
    /// <summary>
    /// The absolute path to the working directory containing the repository files.
    /// Format: /data/{organization}/{name}/tree/
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// The organization or owner name of the repository.
    /// </summary>
    public string Organization { get; set; } = string.Empty;

    /// <summary>
    /// The repository name.
    /// </summary>
    public string RepositoryName { get; set; } = string.Empty;

    /// <summary>
    /// The branch name being processed.
    /// </summary>
    public string BranchName { get; set; } = string.Empty;

    /// <summary>
    /// The repository source type.
    /// </summary>
    public RepositorySourceType SourceType { get; set; } = RepositorySourceType.Git;

    /// <summary>
    /// The original source location for the repository.
    /// </summary>
    public string SourceLocation { get; set; } = string.Empty;

    /// <summary>
    /// The current HEAD commit ID after clone/pull.
    /// </summary>
    public string CommitId { get; set; } = string.Empty;

    /// <summary>
    /// The previous commit ID from the last processing (null for initial processing).
    /// </summary>
    public string? PreviousCommitId { get; set; }

    /// <summary>
    /// The Git URL of the repository.
    /// </summary>
    public string GitUrl { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether this repository source supports incremental updates.
    /// </summary>
    public bool SupportsIncrementalUpdates { get; set; } = true;

    /// <summary>
    /// The local-directory import mode that was actually used.
    /// </summary>
    public LocalDirectoryImportMode LocalDirectoryImportModeUsed { get; set; } = LocalDirectoryImportMode.Copy;

    /// <summary>
    /// Indicates whether this is an incremental update (has previous commit and source supports it).
    /// </summary>
    public bool IsIncremental => SupportsIncrementalUpdates && !string.IsNullOrEmpty(PreviousCommitId);
}
