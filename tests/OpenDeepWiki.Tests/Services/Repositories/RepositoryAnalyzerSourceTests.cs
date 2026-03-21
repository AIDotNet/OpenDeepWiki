using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class RepositoryAnalyzerSourceTests
{
    [Fact]
    public async Task PrepareWorkspaceAsync_ShouldExtractArchiveSourcesIntoIsolatedWorkspace()
    {
        var repositoriesRoot = CreateTempDirectory();
        var archivePath = Path.Combine(CreateTempDirectory(), "repo.zip");
        CreateArchive(archivePath, ("docs/readme.md", "hello archive"));

        var analyzer = CreateAnalyzer(repositoriesRoot);
        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = Guid.NewGuid().ToString(),
            OrgName = "upload",
            RepoName = "archive-repo",
            GitUrl = RepositorySource.EncodeArchivePath(archivePath)
        };

        var workspace = await analyzer.PrepareWorkspaceAsync(repository, "main");

        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory, "docs", "readme.md")));
        Assert.Equal(RepositorySourceType.Archive, workspace.SourceType);
        Assert.False(workspace.SupportsIncrementalUpdates);
        Assert.False(workspace.IsIncremental);
        Assert.False(string.IsNullOrWhiteSpace(workspace.CommitId));
    }

    [Fact]
    public async Task PrepareWorkspaceAsync_ShouldCopyLocalDirectorySourcesByDefault()
    {
        var repositoriesRoot = CreateTempDirectory();
        var sourceRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(sourceRoot, "src"));
        File.WriteAllText(Path.Combine(sourceRoot, "src", "index.ts"), "console.log('copy mode');");

        var analyzer = CreateAnalyzer(
            repositoriesRoot,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = repositoriesRoot,
                AllowedLocalPathRoots = [Path.GetDirectoryName(sourceRoot)!],
                LocalDirectoryImportMode = LocalDirectoryImportMode.Copy
            });

        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = Guid.NewGuid().ToString(),
            OrgName = "local",
            RepoName = "copy-repo",
            GitUrl = RepositorySource.EncodeLocalDirectoryPath(sourceRoot)
        };

        var workspace = await analyzer.PrepareWorkspaceAsync(repository, "main");

        var workspaceFile = Path.Combine(workspace.WorkingDirectory, "src", "index.ts");
        Assert.True(File.Exists(workspaceFile));
        Assert.Equal("console.log('copy mode');", File.ReadAllText(workspaceFile));

        File.WriteAllText(Path.Combine(sourceRoot, "src", "index.ts"), "console.log('source updated');");
        Assert.Equal("console.log('copy mode');", File.ReadAllText(workspaceFile));
    }

    [Fact]
    public async Task PrepareWorkspaceAsync_ShouldReflectSourceChangesWhenLinkModeIsEnabled()
    {
        var repositoriesRoot = CreateTempDirectory();
        var sourceRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(sourceRoot, "linked.txt"), "v1");

        var analyzer = CreateAnalyzer(
            repositoriesRoot,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = repositoriesRoot,
                AllowedLocalPathRoots = [Path.GetDirectoryName(sourceRoot)!],
                LocalDirectoryImportMode = LocalDirectoryImportMode.Link
            });

        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = Guid.NewGuid().ToString(),
            OrgName = "local",
            RepoName = "link-repo",
            GitUrl = RepositorySource.EncodeLocalDirectoryPath(sourceRoot)
        };

        var workspace = await analyzer.PrepareWorkspaceAsync(repository, "main");
        var workspaceFile = Path.Combine(workspace.WorkingDirectory, "linked.txt");

        File.WriteAllText(Path.Combine(sourceRoot, "linked.txt"), "v2");

        Assert.Equal(LocalDirectoryImportMode.Link, workspace.LocalDirectoryImportModeUsed);
        Assert.Equal("v2", File.ReadAllText(workspaceFile));
    }

    [Fact]
    public async Task PrepareWorkspaceAsync_ShouldDisableIncrementalModeForNonGitSources()
    {
        var repositoriesRoot = CreateTempDirectory();
        var sourceRoot = CreateTempDirectory();
        File.WriteAllText(Path.Combine(sourceRoot, "index.md"), "# docs");

        var analyzer = CreateAnalyzer(
            repositoriesRoot,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = repositoriesRoot,
                AllowedLocalPathRoots = [Path.GetDirectoryName(sourceRoot)!]
            });

        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = Guid.NewGuid().ToString(),
            OrgName = "local",
            RepoName = "non-incremental",
            GitUrl = RepositorySource.EncodeLocalDirectoryPath(sourceRoot)
        };

        var workspace = await analyzer.PrepareWorkspaceAsync(repository, "main", previousCommitId: "previous-hash");

        Assert.Equal(RepositorySourceType.LocalDirectory, workspace.SourceType);
        Assert.False(workspace.SupportsIncrementalUpdates);
        Assert.False(workspace.IsIncremental);
    }

    private static RepositoryAnalyzer CreateAnalyzer(string repositoriesRoot, RepositoryAnalyzerOptions? options = null)
    {
        return new RepositoryAnalyzer(
            Options.Create(options ?? new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = repositoriesRoot,
                AllowedLocalPathRoots = []
            }),
            NullLogger<RepositoryAnalyzer>.Instance);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenDeepWiki.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateArchive(string archivePath, params (string path, string content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        using var fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

        foreach (var (path, content) in entries)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }
}
