using OpenDeepWiki.Services.Repositories;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class RepositoryWorkspacePathTests
{
    [Fact]
    public void ForBranch_ShouldUseBranchScopedLayout()
    {
        var options = CreateOptions(CreateTempDirectory());

        var path = RepositoryWorkspacePath.ForBranch(options, "acme", "widgets", "main");

        Assert.Equal(
            Path.Combine(options.RepositoriesDirectory, "acme", "widgets", "branches", "main", "tree"),
            path);
    }

    [Fact]
    public void ForBranch_ShouldStripPathTraversalFromComponents()
    {
        var options = CreateOptions(CreateTempDirectory());

        var path = RepositoryWorkspacePath.ForBranch(options, "../etc", "wid/gets", @"feature\x");

        // Separators become underscores first, so "../etc" ends up as "__etc".
        Assert.Equal(
            Path.Combine(options.RepositoriesDirectory, "__etc", "wid_gets", "branches", "feature_x", "tree"),
            path);
    }

    [Fact]
    public void Resolve_ShouldReturnRequestedBranchWorkspace()
    {
        var repositoriesRoot = CreateTempDirectory();
        var options = CreateOptions(repositoriesRoot);
        var expected = CreateWorkspace(repositoriesRoot, "acme", "widgets", "main");
        CreateWorkspace(repositoriesRoot, "acme", "widgets", "develop");

        Assert.Equal(expected, RepositoryWorkspacePath.Resolve(options, "acme", "widgets", "main"));
    }

    [Fact]
    public void Resolve_WithoutBranch_ShouldReturnMostRecentlyWrittenBranchWorkspace()
    {
        var repositoriesRoot = CreateTempDirectory();
        var options = CreateOptions(repositoriesRoot);
        var stale = CreateWorkspace(repositoriesRoot, "acme", "widgets", "develop");
        var recent = CreateWorkspace(repositoriesRoot, "acme", "widgets", "main");
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-1));
        Directory.SetLastWriteTimeUtc(recent, DateTime.UtcNow);

        Assert.Equal(recent, RepositoryWorkspacePath.Resolve(options, "acme", "widgets", branchName: null));
    }

    [Fact]
    public void Resolve_ShouldFallBackToLegacyFlatWorkspace()
    {
        var repositoriesRoot = CreateTempDirectory();
        var options = CreateOptions(repositoriesRoot);
        var legacy = Path.Combine(repositoriesRoot, "acme", "widgets", "tree");
        Directory.CreateDirectory(legacy);

        Assert.Equal(legacy, RepositoryWorkspacePath.Resolve(options, "acme", "widgets", "main"));
    }

    [Fact]
    public void Resolve_WhenNothingExists_ShouldReturnBranchWorkspacePath()
    {
        var options = CreateOptions(CreateTempDirectory());

        var path = RepositoryWorkspacePath.Resolve(options, "acme", "widgets", "main");

        Assert.Equal(RepositoryWorkspacePath.ForBranch(options, "acme", "widgets", "main"), path);
        Assert.False(Directory.Exists(path));
    }

    private static RepositoryAnalyzerOptions CreateOptions(string repositoriesRoot)
    {
        return new RepositoryAnalyzerOptions { RepositoriesDirectory = repositoriesRoot };
    }

    private static string CreateWorkspace(string repositoriesRoot, string organization, string repository, string branch)
    {
        var path = Path.Combine(repositoriesRoot, organization, repository, "branches", branch, "tree");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenDeepWiki.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
