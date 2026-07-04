using System.IO.Compression;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;
using Xunit;
using GitCloneOptions = LibGit2Sharp.CloneOptions;
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

    [Fact]
    public async Task PrepareWorkspaceAsync_WhenWorkspaceIsPlainDirectoryInsideParentGitRepo_ReclonesTargetBranch()
    {
        var parentRoot = CreateTempDirectory();
        GitRepository.Init(parentRoot);
        var repositoriesRoot = Path.Combine(parentRoot, "data");
        var sourceRoot = CreateTempDirectory();
        var (_, bCommit) = CreateGitRepositoryWithBranches(
            sourceRoot,
            "smart-hw/os_services_develop",
            "smart-hw/rv1106_develop");
        var pollutedWorkspace = GetExpectedWorkspacePath(
            repositoriesRoot,
            "YD_HW/services",
            "youdao-input-event-monitor",
            "smart-hw/rv1106_develop");
        Directory.CreateDirectory(pollutedWorkspace);
        File.WriteAllText(Path.Combine(pollutedWorkspace, "branch.txt"), "polluted parent repository content");

        var analyzer = CreateAnalyzer(
            repositoriesRoot,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = repositoriesRoot,
                AllowedLocalPathRoots = [Path.GetDirectoryName(sourceRoot)!]
            });
        var repository = CreateLocalSourceRepository(sourceRoot);

        var workspace = await analyzer.PrepareWorkspaceAsync(repository, "smart-hw/rv1106_develop");

        Assert.Equal(bCommit, workspace.CommitId);
        Assert.Equal("B branch", File.ReadAllText(Path.Combine(workspace.WorkingDirectory, "branch.txt")));
        using var workspaceRepository = new GitRepository(workspace.WorkingDirectory);
        Assert.Equal(NormalizePath(workspace.WorkingDirectory), NormalizePath(workspaceRepository.Info.WorkingDirectory));
    }

    [Fact]
    public async Task PrepareWorkspaceAsync_WhenWorkspaceHasInvalidGitDirectory_Reclones()
    {
        var repositoriesRoot = CreateTempDirectory();
        var sourceRoot = CreateTempDirectory();
        var (_, bCommit) = CreateGitRepositoryWithBranches(
            sourceRoot,
            "smart-hw/os_services_develop",
            "smart-hw/rv1106_develop");
        var invalidWorkspace = GetExpectedWorkspacePath(
            repositoriesRoot,
            "YD_HW/services",
            "youdao-input-event-monitor",
            "smart-hw/rv1106_develop");
        Directory.CreateDirectory(Path.Combine(invalidWorkspace, ".git"));

        var analyzer = CreateAnalyzer(
            repositoriesRoot,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = repositoriesRoot,
                AllowedLocalPathRoots = [Path.GetDirectoryName(sourceRoot)!]
            });
        var repository = CreateLocalSourceRepository(sourceRoot);

        var workspace = await analyzer.PrepareWorkspaceAsync(repository, "smart-hw/rv1106_develop");

        Assert.Equal(bCommit, workspace.CommitId);
        Assert.Equal("B branch", File.ReadAllText(Path.Combine(workspace.WorkingDirectory, "branch.txt")));
    }

    [Fact]
    public async Task PrepareWorkspaceAsync_WhenWorkspaceOriginPointsAtDifferentSource_Reclones()
    {
        var repositoriesRoot = CreateTempDirectory();
        var rightSourceRoot = CreateTempDirectory();
        var wrongSourceRoot = CreateTempDirectory();
        var (_, rightBCommit) = CreateGitRepositoryWithBranches(
            rightSourceRoot,
            "smart-hw/os_services_develop",
            "smart-hw/rv1106_develop",
            bContent: "right B branch");
        CreateGitRepositoryWithBranches(
            wrongSourceRoot,
            "smart-hw/os_services_develop",
            "smart-hw/rv1106_develop",
            bContent: "wrong B branch");
        var workspacePath = GetExpectedWorkspacePath(
            repositoriesRoot,
            "YD_HW/services",
            "youdao-input-event-monitor",
            "smart-hw/rv1106_develop");
        GitRepository.Clone(
            wrongSourceRoot,
            workspacePath,
            new GitCloneOptions { BranchName = "smart-hw/rv1106_develop" });

        var analyzer = CreateAnalyzer(
            repositoriesRoot,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = repositoriesRoot,
                AllowedLocalPathRoots = [Path.GetDirectoryName(rightSourceRoot)!]
            });
        var repository = CreateLocalSourceRepository(rightSourceRoot);

        var workspace = await analyzer.PrepareWorkspaceAsync(repository, "smart-hw/rv1106_develop");

        Assert.Equal(rightBCommit, workspace.CommitId);
        Assert.Equal("right B branch", File.ReadAllText(Path.Combine(workspace.WorkingDirectory, "branch.txt")));
        using var workspaceRepository = new GitRepository(workspace.WorkingDirectory);
        Assert.Equal(NormalizePath(rightSourceRoot), NormalizePath(workspaceRepository.Network.Remotes["origin"].Url));
    }

    [Fact]
    public async Task PrepareWorkspaceAsync_WhenLocalDirectorySourceIsGitRepoSubdirectory_DoesNotUseParentGitRepository()
    {
        var repositoriesRoot = CreateTempDirectory();
        var parentRoot = CreateTempDirectory();
        GitRepository.Init(parentRoot);
        using (var parentRepository = new GitRepository(parentRoot))
        {
            var signature = new GitSignature("OpenDeepWiki Tests", "tests@opendeepwiki.local", DateTimeOffset.UtcNow);
            Directory.CreateDirectory(Path.Combine(parentRoot, "nested-source"));
            File.WriteAllText(Path.Combine(parentRoot, "nested-source", "branch.txt"), "plain nested source");
            GitCommands.Stage(parentRepository, "nested-source/branch.txt");
            parentRepository.Commit("Parent commit", signature, signature, new GitCommitOptions());
        }

        var sourceSubdirectory = Path.Combine(parentRoot, "nested-source");
        var analyzer = CreateAnalyzer(
            repositoriesRoot,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = repositoriesRoot,
                AllowedLocalPathRoots = [parentRoot]
            });
        var repository = CreateLocalSourceRepository(sourceSubdirectory);

        var workspace = await analyzer.PrepareWorkspaceAsync(repository, "smart-hw/rv1106_develop");

        Assert.False(workspace.SupportsIncrementalUpdates);
        Assert.Equal(64, workspace.CommitId.Length);
        Assert.Equal("plain nested source", File.ReadAllText(Path.Combine(workspace.WorkingDirectory, "branch.txt")));
        Assert.False(Directory.Exists(Path.Combine(workspace.WorkingDirectory, ".git")));
    }

    [Fact]
    public async Task PrepareWorkspaceAsync_WhenLocalDirectorySourceIsGitWorktree_UsesTargetBranchHead()
    {
        var repositoriesRoot = CreateTempDirectory();
        var sourceRoot = CreateTempDirectory();
        var worktreeRoot = CreateTempDirectory();
        Directory.Delete(worktreeRoot);
        var (_, bCommit) = CreateGitRepositoryWithBranches(
            sourceRoot,
            "smart-hw/os_services_develop",
            "smart-hw/rv1106_develop");
        using (var sourceRepository = new GitRepository(sourceRoot))
        {
            sourceRepository.Worktrees.Add(
                "smart-hw/os_services_develop",
                "source-worktree",
                worktreeRoot,
                isLocked: false);
        }

        var analyzer = CreateAnalyzer(
            repositoriesRoot,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = repositoriesRoot,
                AllowedLocalPathRoots = [Path.GetDirectoryName(worktreeRoot)!]
            });
        var repository = CreateLocalSourceRepository(worktreeRoot);

        var workspace = await analyzer.PrepareWorkspaceAsync(repository, "smart-hw/rv1106_develop");

        Assert.True(File.Exists(Path.Combine(worktreeRoot, ".git")));
        Assert.Equal(bCommit, workspace.CommitId);
        Assert.Equal("B branch", File.ReadAllText(Path.Combine(workspace.WorkingDirectory, "branch.txt")));
    }

    [Fact]
    public void BuildGitCliSafeDirectories_WhenSourceIsGitWorktree_IncludesWorktreeGitFileAndCommonGitDir()
    {
        var sourceRoot = CreateTempDirectory();
        var worktreeRoot = CreateTempDirectory();
        Directory.Delete(worktreeRoot);
        CreateGitRepositoryWithBranches(
            sourceRoot,
            "smart-hw/os_services_develop",
            "smart-hw/rv1106_develop");
        using (var sourceRepository = new GitRepository(sourceRoot))
        {
            sourceRepository.Worktrees.Add(
                "smart-hw/os_services_develop",
                "source-worktree",
                worktreeRoot,
                isLocked: false);
        }

        var gitFile = Path.Combine(worktreeRoot, ".git");
        var gitDir = ResolveGitDirPointerForTest(gitFile, worktreeRoot);
        var commonGitDir = ResolveCommonGitDirForTest(gitDir);
        var safeDirectories = InvokeBuildGitCliSafeDirectories(worktreeRoot)
            .Select(NormalizePath)
            .ToArray();

        Assert.Contains(NormalizePath(worktreeRoot), safeDirectories);
        Assert.Contains(NormalizePath(gitFile), safeDirectories);
        Assert.Contains(NormalizePath(gitDir), safeDirectories);
        Assert.Contains(NormalizePath(commonGitDir), safeDirectories);
    }

    [Fact]
    public void BuildGitCliUploadPackArgument_IncludesSafeDirectoriesBeforeUploadPack()
    {
        var sourceRoot = NormalizePath(Path.Combine(CreateTempDirectory(), "source with space"));
        var gitDir = NormalizePath(Path.Combine(sourceRoot, ".git"));
        var argument = InvokeBuildGitCliUploadPackArgument([sourceRoot, gitDir]);
        var processArguments = InvokeBuildGitCliProcessArguments(
            ["clone", "--no-local", argument, "--no-checkout", sourceRoot, "/tmp/target"],
            [sourceRoot, gitDir]);

        Assert.Contains($"-c 'safe.directory={sourceRoot}'", argument);
        Assert.Contains($"-c 'safe.directory={gitDir}'", argument);
        Assert.EndsWith(" upload-pack", argument);
        Assert.Equal("-c", processArguments[0]);
        Assert.Equal($"safe.directory={sourceRoot}", processArguments[1]);
        Assert.Equal("-c", processArguments[2]);
        Assert.Equal($"safe.directory={gitDir}", processArguments[3]);
        Assert.Equal("clone", processArguments[4]);
        Assert.Equal("--no-local", processArguments[5]);
        Assert.Equal(argument, processArguments[6]);
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

    private static IReadOnlyList<string> InvokeBuildGitCliSafeDirectories(params string[] paths)
    {
        var method = typeof(RepositoryAnalyzer).GetMethod(
            "BuildGitCliSafeDirectories",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IReadOnlyList<string>>(method.Invoke(null, [paths]));
    }

    private static string InvokeBuildGitCliUploadPackArgument(IReadOnlyList<string> safeDirectories)
    {
        var method = typeof(RepositoryAnalyzer).GetMethod(
            "BuildGitCliUploadPackArgument",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, [safeDirectories]));
    }

    private static string[] InvokeBuildGitCliProcessArguments(
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> safeDirectories)
    {
        var method = typeof(RepositoryAnalyzer).GetMethod(
            "BuildGitCliProcessArguments",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = Assert.IsAssignableFrom<IEnumerable<string>>(
            method.Invoke(null, [arguments, safeDirectories]));
        return result.ToArray();
    }

    private static string ResolveGitDirPointerForTest(string gitFile, string worktreePath)
    {
        var firstLine = File.ReadLines(gitFile).First();
        Assert.StartsWith("gitdir:", firstLine, StringComparison.OrdinalIgnoreCase);
        var gitDir = firstLine["gitdir:".Length..].Trim();
        return Path.IsPathRooted(gitDir)
            ? NormalizePath(gitDir)
            : NormalizePath(Path.Combine(worktreePath, gitDir));
    }

    private static string ResolveCommonGitDirForTest(string gitDir)
    {
        var commonDir = File.ReadLines(Path.Combine(gitDir, "commondir")).First().Trim();
        return Path.IsPathRooted(commonDir)
            ? NormalizePath(commonDir)
            : NormalizePath(Path.Combine(gitDir, commonDir));
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

    private static Repository CreateLocalSourceRepository(string sourceRoot)
    {
        return new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = Guid.NewGuid().ToString(),
            OrgName = "YD_HW/services",
            RepoName = "youdao-input-event-monitor",
            GitUrl = RepositorySource.EncodeLocalDirectoryPath(sourceRoot)
        };
    }

    private static string GetExpectedWorkspacePath(
        string repositoriesRoot,
        string orgName,
        string repositoryName,
        string branchName)
    {
        return Path.Combine(
            repositoriesRoot,
            SanitizePathComponent(orgName),
            SanitizePathComponent(repositoryName),
            "branches",
            SanitizePathComponent(branchName),
            "tree");
    }

    private static string SanitizePathComponent(string component)
    {
        return component
            .Replace('/', '_')
            .Replace('\\', '_')
            .Replace("..", "_")
            .Trim();
    }

    private static string NormalizePath(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            path = uri.LocalPath;
        }

        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static (string ACommit, string BCommit) CreateGitRepositoryWithBranches(
        string repositoryPath,
        string branchA,
        string branchB,
        string aContent = "A branch",
        string bContent = "B branch")
    {
        GitRepository.Init(repositoryPath);
        using var repository = new GitRepository(repositoryPath);
        var signature = new GitSignature("OpenDeepWiki Tests", "tests@opendeepwiki.local", DateTimeOffset.UtcNow);

        File.WriteAllText(Path.Combine(repositoryPath, "branch.txt"), aContent);
        GitCommands.Stage(repository, "branch.txt");
        var commitOptions = new GitCommitOptions();
        var aCommit = repository.Commit("A branch", signature, signature, commitOptions);
        repository.Branches.Add(branchA, aCommit);

        var bBranch = repository.Branches.Add(branchB, aCommit);
        GitCommands.Checkout(repository, bBranch);
        File.WriteAllText(Path.Combine(repositoryPath, "branch.txt"), bContent);
        GitCommands.Stage(repository, "branch.txt");
        var bCommit = repository.Commit("B branch", signature, signature, commitOptions);

        return (aCommit.Sha, bCommit.Sha);
    }
}
