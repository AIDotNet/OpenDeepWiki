using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;
using Xunit;
using GitCommitOptions = LibGit2Sharp.CommitOptions;
using GitCommands = LibGit2Sharp.Commands;
using GitRepository = LibGit2Sharp.Repository;
using GitSignature = LibGit2Sharp.Signature;

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

    [Fact]
    public async Task PrepareWorkspaceAsync_WhenRepositoryHasMultipleBranches_UsesTargetBranchContent()
    {
        var repositoriesRoot = CreateTempDirectory();
        var sourceRoot = CreateTempDirectory();
        var (aCommit, bCommit) = CreateGitRepositoryWithBranches(
            sourceRoot,
            "smart-hw/os_services_develop",
            "smart-hw/rv1106_develop");
        var analyzer = CreateAnalyzer(repositoriesRoot);
        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = Guid.NewGuid().ToString(),
            OrgName = "YD_HW/services",
            RepoName = "youdao-input-event-monitor",
            GitUrl = sourceRoot
        };

        var aWorkspace = await analyzer.PrepareWorkspaceAsync(repository, "smart-hw/os_services_develop");
        var bWorkspace = await analyzer.PrepareWorkspaceAsync(repository, "smart-hw/rv1106_develop");

        Assert.Equal(aCommit, aWorkspace.CommitId);
        Assert.Equal("A branch", File.ReadAllText(Path.Combine(aWorkspace.WorkingDirectory, "branch.txt")));
        Assert.Equal(bCommit, bWorkspace.CommitId);
        Assert.Equal("B branch", File.ReadAllText(Path.Combine(bWorkspace.WorkingDirectory, "branch.txt")));
        Assert.NotEqual(aWorkspace.WorkingDirectory, bWorkspace.WorkingDirectory);
    }

    [Fact]
    public async Task PrepareWorkspaceAsync_WhenLocalDirectorySourceIsGitRepository_UsesTargetBranchHead()
    {
        var repositoriesRoot = CreateTempDirectory();
        var sourceRoot = CreateTempDirectory();
        var (aCommit, bCommit) = CreateGitRepositoryWithBranches(
            sourceRoot,
            "smart-hw/os_services_develop",
            "smart-hw/rv1106_develop");
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
            OrgName = "YD_HW/services",
            RepoName = "youdao-input-event-monitor",
            GitUrl = RepositorySource.EncodeLocalDirectoryPath(sourceRoot)
        };

        var aWorkspace = await analyzer.PrepareWorkspaceAsync(repository, "smart-hw/os_services_develop");
        var bWorkspace = await analyzer.PrepareWorkspaceAsync(repository, "smart-hw/rv1106_develop");
        var bRemoteHead = await analyzer.GetRemoteBranchHeadCommitAsync(repository, "smart-hw/rv1106_develop");

        Assert.Equal(RepositorySourceType.LocalDirectory, bWorkspace.SourceType);
        Assert.True(bWorkspace.SupportsIncrementalUpdates);
        Assert.Equal(aCommit, aWorkspace.CommitId);
        Assert.Equal("A branch", File.ReadAllText(Path.Combine(aWorkspace.WorkingDirectory, "branch.txt")));
        Assert.Equal(bCommit, bWorkspace.CommitId);
        Assert.Equal(bCommit, bRemoteHead);
        Assert.Equal("B branch", File.ReadAllText(Path.Combine(bWorkspace.WorkingDirectory, "branch.txt")));
        Assert.NotEqual(aWorkspace.WorkingDirectory, bWorkspace.WorkingDirectory);
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

    private static (string ACommit, string BCommit) CreateGitRepositoryWithBranches(
        string repositoryPath,
        string branchA,
        string branchB)
    {
        GitRepository.Init(repositoryPath);
        using var repository = new GitRepository(repositoryPath);
        var signature = new GitSignature("OpenDeepWiki Tests", "tests@opendeepwiki.local", DateTimeOffset.UtcNow);

        File.WriteAllText(Path.Combine(repositoryPath, "branch.txt"), "A branch");
        GitCommands.Stage(repository, "branch.txt");
        var commitOptions = new GitCommitOptions();
        var aCommit = repository.Commit("A branch", signature, signature, commitOptions);
        repository.Branches.Add(branchA, aCommit);

        var bBranch = repository.Branches.Add(branchB, aCommit);
        GitCommands.Checkout(repository, bBranch);
        File.WriteAllText(Path.Combine(repositoryPath, "branch.txt"), "B branch");
        GitCommands.Stage(repository, "branch.txt");
        var bCommit = repository.Commit("B branch", signature, signature, commitOptions);

        return (aCommit.Sha, bCommit.Sha);
    }
}
