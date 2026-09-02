namespace OpenDeepWiki.Services.Repositories;

/// <summary>
/// Resolves the on-disk workspace (source checkout) of a repository.
/// Layout written by <see cref="RepositoryAnalyzer"/>:
/// {RepositoriesDirectory}/{organization}/{name}/branches/{branch}/tree/
/// </summary>
public static class RepositoryWorkspacePath
{
    private const string BranchesDirectoryName = "branches";
    private const string TreeDirectoryName = "tree";

    /// <summary>
    /// Gets the workspace path of a specific branch, whether or not it exists on disk.
    /// </summary>
    public static string ForBranch(
        RepositoryAnalyzerOptions options,
        string organization,
        string repositoryName,
        string branchName)
    {
        return Path.Combine(
            RepositoryRoot(options, organization, repositoryName),
            BranchesDirectoryName,
            Sanitize(branchName),
            TreeDirectoryName);
    }

    /// <summary>
    /// Resolves the best workspace available to a reader (MCP tools, chat services).
    /// Prefers the requested branch, then any other branch workspace, and finally the
    /// flat layout used before branch workspaces were introduced. The returned path is
    /// not guaranteed to exist -- callers still have to check.
    /// </summary>
    public static string Resolve(
        RepositoryAnalyzerOptions options,
        string organization,
        string repositoryName,
        string? branchName)
    {
        var repositoryRoot = RepositoryRoot(options, organization, repositoryName);
        var hasBranch = !string.IsNullOrWhiteSpace(branchName);

        var branchWorkspace = hasBranch
            ? Path.Combine(repositoryRoot, BranchesDirectoryName, Sanitize(branchName!), TreeDirectoryName)
            : null;

        if (branchWorkspace != null && Directory.Exists(branchWorkspace))
        {
            return branchWorkspace;
        }

        // Callers without branch context (e.g. the MCP scope carries owner/repo only)
        // fall back to the most recently written branch workspace.
        if (!hasBranch && NewestBranchWorkspace(repositoryRoot) is { } newestWorkspace)
        {
            return newestWorkspace;
        }

        var legacyWorkspace = Path.Combine(repositoryRoot, TreeDirectoryName);
        if (Directory.Exists(legacyWorkspace))
        {
            return legacyWorkspace;
        }

        return branchWorkspace ?? legacyWorkspace;
    }

    private static string RepositoryRoot(
        RepositoryAnalyzerOptions options,
        string organization,
        string repositoryName)
    {
        return Path.Combine(options.RepositoriesDirectory, Sanitize(organization), Sanitize(repositoryName));
    }

    private static string? NewestBranchWorkspace(string repositoryRoot)
    {
        var branchesRoot = Path.Combine(repositoryRoot, BranchesDirectoryName);
        if (!Directory.Exists(branchesRoot))
        {
            return null;
        }

        return Directory.EnumerateDirectories(branchesRoot)
            .Select(branchDirectory => Path.Combine(branchDirectory, TreeDirectoryName))
            .Where(Directory.Exists)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Strips path separators and traversal sequences from a path component. Readers use a
    /// placeholder for empty input instead of throwing, because they resolve untrusted
    /// owner/repo values coming straight from a request.
    /// </summary>
    private static string Sanitize(string? component)
    {
        var sanitized = (component ?? string.Empty)
            .Replace('/', '_')
            .Replace('\\', '_')
            .Replace("..", "_")
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }
}
