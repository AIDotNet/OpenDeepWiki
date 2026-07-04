using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Entities;
using GitRepository = LibGit2Sharp.Repository;

namespace OpenDeepWiki.Services.Repositories;

/// <summary>
/// Configuration options for the repository analyzer.
/// </summary>
public class RepositoryAnalyzerOptions
{
    /// <summary>
    /// The base directory for storing repository clones.
    /// Default: /data on Linux, C:\data on Windows.
    /// </summary>
    public string RepositoriesDirectory { get; set; } = 
        OperatingSystem.IsWindows() ? @"C:\data" : "/data";

    /// <summary>
    /// Whether to clean up the working directory after processing.
    /// </summary>
    public bool CleanupAfterProcessing { get; set; } = false;

    /// <summary>
    /// Maximum retry attempts for clone/pull operations.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts in milliseconds.
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Local directory roots allowed for import.
    /// </summary>
    public string[] AllowedLocalPathRoots { get; set; } = [];

    /// <summary>
    /// Local directory import mode.
    /// </summary>
    public LocalDirectoryImportMode LocalDirectoryImportMode { get; set; } = LocalDirectoryImportMode.Copy;
}

/// <summary>
/// Implementation of IRepositoryAnalyzer using LibGit2Sharp.
/// Handles cloning, updating, and analyzing Git repositories.
/// </summary>
public class RepositoryAnalyzer : IRepositoryAnalyzer
{
    private readonly RepositoryAnalyzerOptions _options;
    private readonly ILogger<RepositoryAnalyzer> _logger;

    public RepositoryAnalyzer(
        IOptions<RepositoryAnalyzerOptions> options,
        ILogger<RepositoryAnalyzer> logger)
    {
        _options = options.Value;
        _logger = logger;

        _logger.LogDebug(
            "RepositoryAnalyzer initialized. RepositoriesDirectory: {RepoDir}, CleanupAfterProcessing: {Cleanup}, MaxRetryAttempts: {MaxRetry}",
            _options.RepositoriesDirectory, _options.CleanupAfterProcessing, _options.MaxRetryAttempts);
    }

    /// <inheritdoc />
    public async Task<string?> GetRemoteBranchHeadCommitAsync(
        Entities.Repository repository,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            throw new ArgumentException("Branch name cannot be empty.", nameof(branchName));
        }

        var sourceInfo = RepositorySource.Parse(repository.GitUrl);
        var exactFailureReason = "not evaluated";
        if (sourceInfo.SourceType == RepositorySourceType.LocalDirectory &&
            TryResolveLocalGitSource(sourceInfo.Location, out var localGitSource, out exactFailureReason))
        {
            if (localGitSource.AccessMode == LocalGitAccessMode.LibGit2)
            {
                using var localRepository = new GitRepository(localGitSource.RepositoryPath);
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    return ResolveLocalSourceBranchTip(localRepository, branchName)?.Sha;
                }, cancellationToken);
            }

            return (await GetGitCliBranchHeadCommitAsync(localGitSource.RepositoryPath, branchName, cancellationToken))?.CommitId;
        }

        if (sourceInfo.SourceType == RepositorySourceType.LocalDirectory)
        {
            _logger.LogInformation(
                "Local source remote HEAD lookup skipped because decoded source is not an exact Git source. Repository: {Org}/{Repo}, Branch: {Branch}, SourcePath: {SourcePath}, Reason: {Reason}",
                repository.OrgName, repository.RepoName, branchName, sourceInfo.Location, exactFailureReason);
        }

        if (sourceInfo.SourceType != RepositorySourceType.Git)
        {
            _logger.LogDebug(
                "Skipping remote HEAD lookup for non-git source. Repository: {Org}/{Repo}, SourceType: {SourceType}",
                repository.OrgName, repository.RepoName, sourceInfo.SourceType);
            return null;
        }

        var credentials = BuildCredentials(repository);
        var branchRefName = $"refs/heads/{branchName}";

        _logger.LogDebug(
            "Looking up remote HEAD. Repository: {Org}/{Repo}, Branch: {Branch}, Ref: {Ref}",
            repository.OrgName, repository.RepoName, branchName, branchRefName);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var references = credentials == null
                ? GitRepository.ListRemoteReferences(sourceInfo.Location)
                : GitRepository.ListRemoteReferences(sourceInfo.Location, (_, _, _) => credentials);

            var branchReference = references.FirstOrDefault(reference =>
                string.Equals(reference.CanonicalName, branchRefName, StringComparison.Ordinal));

            return branchReference?.TargetIdentifier;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RepositoryWorkspace> PrepareWorkspaceAsync(
        Entities.Repository repository,
        string branchName,
        string? previousCommitId = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var sourceInfo = RepositorySource.Parse(repository.GitUrl);
        var workspace = new RepositoryWorkspace
        {
            Organization = repository.OrgName,
            RepositoryName = repository.RepoName,
            BranchName = branchName,
            SourceType = sourceInfo.SourceType,
            SourceLocation = sourceInfo.Location,
            GitUrl = sourceInfo.SourceType == RepositorySourceType.Git ? sourceInfo.Location : string.Empty,
            PreviousCommitId = previousCommitId,
            WorkingDirectory = GetWorkingDirectory(repository.OrgName, repository.RepoName, branchName),
            SupportsIncrementalUpdates = sourceInfo.SourceType == RepositorySourceType.Git,
            LocalDirectoryImportModeUsed = LocalDirectoryImportMode.Copy
        };

        _logger.LogInformation(
            "Preparing workspace. Repository: {Org}/{Repo}, Branch: {Branch}, SourceType: {SourceType}, SourceLocation: {SourceLocation}, WorkingDirectory: {Path}, PreviousCommit: {PreviousCommit}",
            workspace.Organization, workspace.RepositoryName, branchName,
            workspace.SourceType, workspace.SourceLocation,
            workspace.WorkingDirectory, previousCommitId ?? "none");

        // Ensure the parent directory exists
        var parentDir = Path.GetDirectoryName(workspace.WorkingDirectory);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            _logger.LogDebug("Creating parent directory: {ParentDir}", parentDir);
            Directory.CreateDirectory(parentDir);
        }

        if (workspace.SourceType == RepositorySourceType.Git)
        {
            // Build credentials if provided
            var credentials = BuildCredentials(repository);
            var hasCredentials = credentials != null;
            _logger.LogDebug("Credentials configured: {HasCredentials}", hasCredentials);

            // Clone or pull the repository
            var repoExists = Directory.Exists(workspace.WorkingDirectory) &&
                             Directory.Exists(Path.Combine(workspace.WorkingDirectory, ".git"));

            if (repoExists)
            {
                _logger.LogDebug("Repository exists locally, pulling latest changes");
                await PullRepositoryAsync(workspace, credentials, cancellationToken);
            }
            else
            {
                _logger.LogDebug("Repository does not exist locally, cloning");
                await CloneRepositoryAsync(workspace, credentials, cancellationToken);
            }

            // Get the current HEAD commit ID
            workspace.CommitId = GetHeadCommitId(workspace.WorkingDirectory);
        }
        else if (workspace.SourceType == RepositorySourceType.Archive)
        {
            await PrepareArchiveWorkspaceAsync(workspace, cancellationToken);
            workspace.CommitId = ComputeDirectorySnapshotId(workspace.WorkingDirectory);
        }
        else if (TryResolveLocalGitSource(workspace.SourceLocation, out var localGitSource, out var localGitFailureReason))
        {
            await PrepareLocalGitWorkspaceAsync(workspace, localGitSource, cancellationToken);
            workspace.CommitId = GetHeadCommitId(workspace.WorkingDirectory);
            workspace.SupportsIncrementalUpdates = true;
        }
        else
        {
            if (workspace.SourceType == RepositorySourceType.LocalDirectory)
            {
                _logger.LogWarning(
                    "Decoded local source is not an exact Git source; falling back to local directory snapshot. SourcePath: {SourcePath}, Branch: {Branch}, WorkingDirectory: {Path}, Reason: {Reason}",
                    workspace.SourceLocation, workspace.BranchName, workspace.WorkingDirectory, localGitFailureReason);
            }

            await PrepareLocalDirectoryWorkspaceAsync(workspace, cancellationToken);
            workspace.CommitId = ComputeDirectorySnapshotId(workspace.WorkingDirectory);
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Workspace prepared successfully. Repository: {Org}/{Repo}, CurrentCommit: {CommitId}, PreviousCommit: {PreviousCommitId}, IsIncremental: {IsIncremental}, Duration: {Duration}ms",
            workspace.Organization, workspace.RepositoryName,
            workspace.CommitId, workspace.PreviousCommitId ?? "none",
            workspace.IsIncremental, stopwatch.ElapsedMilliseconds);

        return workspace;
    }


    /// <inheritdoc />
    public Task CleanupWorkspaceAsync(RepositoryWorkspace workspace, CancellationToken cancellationToken = default)
    {
        if (!_options.CleanupAfterProcessing)
        {
            _logger.LogDebug(
                "Cleanup disabled, keeping workspace. Path: {Path}, Repository: {Org}/{Repo}",
                workspace.WorkingDirectory, workspace.Organization, workspace.RepositoryName);
            return Task.CompletedTask;
        }

        if (Directory.Exists(workspace.WorkingDirectory))
        {
            _logger.LogInformation(
                "Cleaning up workspace. Path: {Path}, Repository: {Org}/{Repo}",
                workspace.WorkingDirectory, workspace.Organization, workspace.RepositoryName);
            
            var stopwatch = Stopwatch.StartNew();
            try
            {
                // Force delete all files including read-only ones (common in .git folder)
                DeleteDirectoryRecursive(workspace.WorkingDirectory);
                stopwatch.Stop();
                _logger.LogInformation(
                    "Workspace cleanup completed. Path: {Path}, Duration: {Duration}ms",
                    workspace.WorkingDirectory, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(ex, 
                    "Failed to cleanup workspace. Path: {Path}, Duration: {Duration}ms",
                    workspace.WorkingDirectory, stopwatch.ElapsedMilliseconds);
            }
        }
        else
        {
            _logger.LogDebug("Workspace directory does not exist, nothing to cleanup. Path: {Path}", 
                workspace.WorkingDirectory);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string[]> GetChangedFilesAsync(
        RepositoryWorkspace workspace,
        string? fromCommitId,
        string toCommitId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!workspace.SupportsIncrementalUpdates)
        {
            _logger.LogInformation(
                "Repository source does not support incremental updates. Repository: {Org}/{Repo}, SourceType: {SourceType}",
                workspace.Organization, workspace.RepositoryName, workspace.SourceType);
            return Task.FromResult(Array.Empty<string>());
        }

        if (string.IsNullOrEmpty(fromCommitId))
        {
            _logger.LogInformation(
                "No previous commit specified, returning all tracked files. Repository: {Org}/{Repo}",
                workspace.Organization, workspace.RepositoryName);
            var allFiles = GetAllTrackedFiles(workspace.WorkingDirectory);
            stopwatch.Stop();
            _logger.LogInformation(
                "Retrieved all tracked files. Count: {Count}, Duration: {Duration}ms",
                allFiles.Length, stopwatch.ElapsedMilliseconds);
            return Task.FromResult(allFiles);
        }

        _logger.LogInformation(
            "Getting changed files. Repository: {Org}/{Repo}, FromCommit: {FromCommit}, ToCommit: {ToCommit}",
            workspace.Organization, workspace.RepositoryName, fromCommitId, toCommitId);

        var changedFiles = GetChangedFilesBetweenCommits(
            workspace.WorkingDirectory, 
            fromCommitId, 
            toCommitId);

        stopwatch.Stop();
        _logger.LogInformation(
            "Changed files retrieved. Count: {Count}, Duration: {Duration}ms",
            changedFiles.Length, stopwatch.ElapsedMilliseconds);

        if (changedFiles.Length > 0 && changedFiles.Length <= 20)
        {
            _logger.LogDebug("Changed files: {Files}", string.Join(", ", changedFiles));
        }

        return Task.FromResult(changedFiles);
    }

    /// <summary>
    /// Gets the working directory path for a repository.
    /// Format: {RepositoriesDirectory}/{organization}/{name}/branches/{branch}/tree/
    /// </summary>
    private string GetWorkingDirectory(string organization, string repositoryName, string branchName)
    {
        // Sanitize organization and repository names to prevent path traversal
        var safeOrg = SanitizePathComponent(organization);
        var safeRepo = SanitizePathComponent(repositoryName);
        var safeBranch = SanitizePathComponent(branchName);

        return Path.Combine(_options.RepositoriesDirectory, safeOrg, safeRepo, "branches", safeBranch, "tree");
    }

    private async Task PrepareArchiveWorkspaceAsync(
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(workspace.SourceLocation) || !File.Exists(workspace.SourceLocation))
        {
            throw new FileNotFoundException($"Archive source not found: {workspace.SourceLocation}");
        }

        if (Directory.Exists(workspace.WorkingDirectory))
        {
            DeleteDirectoryRecursive(workspace.WorkingDirectory);
        }

        Directory.CreateDirectory(workspace.WorkingDirectory);
        ZipFile.ExtractToDirectory(workspace.SourceLocation, workspace.WorkingDirectory, overwriteFiles: true);
        await Task.CompletedTask;
    }

    private async Task PrepareLocalDirectoryWorkspaceAsync(
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(workspace.SourceLocation) || !Directory.Exists(workspace.SourceLocation))
        {
            throw new DirectoryNotFoundException($"Local directory source not found: {workspace.SourceLocation}");
        }

        if (Directory.Exists(workspace.WorkingDirectory))
        {
            DeleteDirectoryRecursive(workspace.WorkingDirectory);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(workspace.WorkingDirectory)!);

        if (_options.LocalDirectoryImportMode == LocalDirectoryImportMode.Link &&
            await TryCreateDirectoryLinkAsync(workspace.WorkingDirectory, workspace.SourceLocation, cancellationToken))
        {
            workspace.LocalDirectoryImportModeUsed = LocalDirectoryImportMode.Link;
            return;
        }

        workspace.LocalDirectoryImportModeUsed = LocalDirectoryImportMode.Copy;
        CopyDirectory(workspace.SourceLocation, workspace.WorkingDirectory);
    }

    private async Task PrepareLocalGitWorkspaceAsync(
        RepositoryWorkspace workspace,
        LocalGitSource localGitSource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(localGitSource.RepositoryPath) || !Directory.Exists(localGitSource.RepositoryPath))
        {
            throw new DirectoryNotFoundException($"Local git source not found: {localGitSource.RepositoryPath}");
        }

        _logger.LogInformation(
            "Preparing local git workspace from target branch. SourcePath: {SourcePath}, Branch: {Branch}, TargetPath: {Path}, AccessMode: {AccessMode}",
            localGitSource.RepositoryPath, workspace.BranchName, workspace.WorkingDirectory, localGitSource.AccessMode);

        if (localGitSource.AccessMode == LocalGitAccessMode.GitCli)
        {
            await PrepareLocalGitWorkspaceWithCliAsync(workspace, localGitSource.RepositoryPath, cancellationToken);
            return;
        }

        using var sourceRepository = new GitRepository(localGitSource.RepositoryPath);
        var targetTip = ResolveLocalSourceBranchTip(sourceRepository, workspace.BranchName)
            ?? throw new InvalidOperationException(
                $"Branch '{workspace.BranchName}' not found in local git source: {localGitSource.RepositoryPath}");

        _logger.LogInformation(
            "Resolved local git source branch. SourcePath: {SourcePath}, Branch: {Branch}, Commit: {CommitId}",
            localGitSource.RepositoryPath, workspace.BranchName, targetTip.Sha);

        var originalGitUrl = workspace.GitUrl;
        workspace.GitUrl = localGitSource.RepositoryPath;

        try
        {
            if (IsValidWorkspaceForSource(workspace.WorkingDirectory, localGitSource.RepositoryPath, out var workspaceReason))
            {
                await PullRepositoryAsync(workspace, credentials: null, cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "Existing local git workspace is missing, invalid, or points at a different source; recloning. SourcePath: {SourcePath}, Branch: {Branch}, TargetPath: {Path}, Reason: {Reason}",
                    localGitSource.RepositoryPath, workspace.BranchName, workspace.WorkingDirectory, workspaceReason);

                DeleteWorkspaceDirectoryWithinRepositoryRoot(workspace.WorkingDirectory, localGitSource.RepositoryPath);
                await CloneRepositoryAsync(workspace, credentials: null, cancellationToken);
            }

            workspace.LocalDirectoryImportModeUsed = LocalDirectoryImportMode.Copy;
        }
        finally
        {
            workspace.GitUrl = originalGitUrl;
        }
    }

    private async Task PrepareLocalGitWorkspaceWithCliAsync(
        RepositoryWorkspace workspace,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var sourceSafeDirectories = BuildGitCliSafeDirectories(sourcePath);
        var workspaceSafeDirectories = BuildGitCliSafeDirectories(workspace.WorkingDirectory, sourcePath);
        var sourceUploadPackArgument = BuildGitCliUploadPackArgument(sourceSafeDirectories);

        var targetBranch = await GetGitCliBranchHeadCommitAsync(
                sourcePath,
                workspace.BranchName,
                cancellationToken,
                sourceSafeDirectories)
            ?? throw new InvalidOperationException(
                $"Branch '{workspace.BranchName}' not found in local git source: {sourcePath}");

        _logger.LogInformation(
            "Resolved local git source branch via git CLI. SourcePath: {SourcePath}, Branch: {Branch}, SourceRef: {SourceRef}, FetchRefSpec: {FetchRefSpec}, Commit: {CommitId}",
            sourcePath, workspace.BranchName, targetBranch.SourceRef, targetBranch.FetchRefSpec, targetBranch.CommitId);

        if (await IsValidGitCliWorkspaceForSourceAsync(
                workspace.WorkingDirectory,
                sourcePath,
                workspaceSafeDirectories,
                cancellationToken))
        {
            await FetchGitCliBranchAsync(
                workspace.WorkingDirectory,
                sourceUploadPackArgument,
                targetBranch,
                workspaceSafeDirectories,
                cancellationToken);
        }
        else
        {
            var reason = await GetGitCliWorkspaceInvalidReasonAsync(
                workspace.WorkingDirectory,
                sourcePath,
                workspaceSafeDirectories,
                cancellationToken);
            _logger.LogWarning(
                "Existing local git workspace is missing, invalid, or points at a different source; recloning. SourcePath: {SourcePath}, Branch: {Branch}, TargetPath: {Path}, Reason: {Reason}",
                sourcePath, workspace.BranchName, workspace.WorkingDirectory, reason);

            DeleteWorkspaceDirectoryWithinRepositoryRoot(workspace.WorkingDirectory, sourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(workspace.WorkingDirectory)!);
            await RunGitCliAsync(
                Directory.GetCurrentDirectory(),
                [
                    "clone",
                    "--no-local",
                    sourceUploadPackArgument,
                    "--no-checkout",
                    sourcePath,
                    workspace.WorkingDirectory
                ],
                cancellationToken,
                throwOnError: true,
                safeDirectories: BuildGitCliSafeDirectories(sourcePath, workspace.WorkingDirectory));
        }

        workspaceSafeDirectories = BuildGitCliSafeDirectories(workspace.WorkingDirectory, sourcePath);
        await FetchGitCliBranchAsync(
            workspace.WorkingDirectory,
            sourceUploadPackArgument,
            targetBranch,
            workspaceSafeDirectories,
            cancellationToken);
        await EnsureGitCliWorkspaceHasCommitAsync(
            workspace.WorkingDirectory,
            targetBranch.CommitId,
            workspaceSafeDirectories,
            cancellationToken,
            targetBranch.FetchRefSpec);
        await RunGitCliAsync(
            workspace.WorkingDirectory,
            ["checkout", "-B", workspace.BranchName, targetBranch.CommitId],
            cancellationToken,
            throwOnError: true,
            safeDirectories: workspaceSafeDirectories);
        await RunGitCliAsync(
            workspace.WorkingDirectory,
            ["reset", "--hard", targetBranch.CommitId],
            cancellationToken,
            throwOnError: true,
            safeDirectories: workspaceSafeDirectories);

        workspace.LocalDirectoryImportModeUsed = LocalDirectoryImportMode.Copy;
    }

    private void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        var sourceInfo = new DirectoryInfo(sourceDirectory);
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in sourceInfo.GetDirectories())
        {
            if (IsSymbolicLink(directory))
            {
                _logger.LogWarning(
                    "Skipping symbolic link directory while copying local repository. Source: {SourcePath}, LinkTarget: {LinkTarget}",
                    directory.FullName,
                    directory.LinkTarget);
                continue;
            }

            if (IsBrokenSymbolicLink(directory))
            {
                _logger.LogWarning(
                    "Skipping broken symbolic link while copying local repository directory. Source: {SourcePath}, LinkTarget: {LinkTarget}",
                    directory.FullName,
                    directory.LinkTarget);
                continue;
            }

            CopyDirectory(directory.FullName, Path.Combine(destinationDirectory, directory.Name));
        }

        foreach (var file in sourceInfo.GetFiles())
        {
            if (IsBrokenSymbolicLink(file))
            {
                _logger.LogWarning(
                    "Skipping broken symbolic link while copying local repository file. Source: {SourcePath}, LinkTarget: {LinkTarget}",
                    file.FullName,
                    file.LinkTarget);
                continue;
            }

            TryCopyFile(file, Path.Combine(destinationDirectory, file.Name));
        }
    }

    private void TryCopyFile(FileInfo file, string destinationPath)
    {
        try
        {
            file.CopyTo(destinationPath, overwrite: true);
        }
        catch (FileNotFoundException) when (IsSymbolicLink(file) || !File.Exists(file.FullName))
        {
            _logger.LogWarning(
                "Skipping unresolved symbolic link while copying local repository file. Source: {SourcePath}, LinkTarget: {LinkTarget}",
                file.FullName,
                file.LinkTarget);
        }
    }

    private static bool IsBrokenSymbolicLink(FileSystemInfo fileSystemInfo)
    {
        if (string.IsNullOrEmpty(fileSystemInfo.LinkTarget))
        {
            return false;
        }

        return !File.Exists(fileSystemInfo.FullName) && !Directory.Exists(fileSystemInfo.FullName);
    }

    private static bool IsSymbolicLink(FileSystemInfo fileSystemInfo)
    {
        return !string.IsNullOrEmpty(fileSystemInfo.LinkTarget) ||
               fileSystemInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private bool TryResolveLocalGitSource(
        string path,
        out LocalGitSource localGitSource,
        out string failureReason)
    {
        localGitSource = default;

        if (TryOpenExactGitWorkdir(path, out var repository, out failureReason))
        {
            repository.Dispose();
            localGitSource = new LocalGitSource(path, LocalGitAccessMode.LibGit2);
            _logger.LogInformation(
                "Decoded local source path is an exact Git workdir. SourcePath: {SourcePath}, AccessMode: {AccessMode}",
                path, localGitSource.AccessMode);
            return true;
        }

        if (TryResolveGitCliExactWorkdir(path, out var cliTopLevel, out var cliFailureReason))
        {
            localGitSource = new LocalGitSource(path, LocalGitAccessMode.GitCli);
            failureReason = $"LibGit2 exact workdir failed: {failureReason}; git CLI exact workdir succeeded: {cliTopLevel}";
            _logger.LogWarning(
                "Decoded local source path is an exact Git workdir via git CLI fallback. SourcePath: {SourcePath}, TopLevel: {TopLevel}, LibGit2Reason: {LibGit2Reason}",
                path, cliTopLevel, failureReason);
            return true;
        }

        failureReason = $"LibGit2 exact workdir failed: {failureReason}; git CLI exact workdir failed: {cliFailureReason}";
        return false;
    }

    private static Commit? ResolveLocalSourceBranchTip(GitRepository repository, string branchName)
    {
        return repository.Branches[branchName]?.Tip ??
               repository.Branches[$"origin/{branchName}"]?.Tip;
    }

    private static bool IsValidWorkspaceForSource(string workspacePath, string sourcePath, out string reason)
    {
        if (!TryOpenExactGitWorkdir(workspacePath, out var repository, out reason))
        {
            return false;
        }

        using (repository)
        {
            var remote = repository.Network.Remotes["origin"];
            if (remote == null)
            {
                reason = "workspace has no origin remote";
                return false;
            }

            if (!LocalPathEquals(remote.Url, sourcePath))
            {
                reason = $"workspace origin '{remote.Url}' does not match source '{sourcePath}'";
                return false;
            }

            reason = "workspace is an exact Git workdir and origin matches source";
            return true;
        }
    }

    private static bool TryOpenExactGitWorkdir(string path, out GitRepository repository, out string reason)
    {
        repository = null!;
        reason = "unknown";

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            reason = $"path does not exist or is not a directory: {path}";
            return false;
        }

        try
        {
            var candidate = new GitRepository(path);
            var workingDirectory = candidate.Info.WorkingDirectory;
            if (!LocalPathEquals(workingDirectory, path))
            {
                reason = $"opened Git workdir root '{workingDirectory}' does not equal requested path '{path}'";
                candidate.Dispose();
                return false;
            }

            repository = candidate;
            reason = "exact Git workdir";
            return true;
        }
        catch (RepositoryNotFoundException ex)
        {
            reason = ex.Message;
            return false;
        }
        catch (LibGit2SharpException ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private bool TryResolveGitCliExactWorkdir(string path, out string topLevel, out string reason)
    {
        topLevel = string.Empty;

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            reason = $"path does not exist or is not a directory: {path}";
            return false;
        }

        var result = RunGitCli(
            path,
            ["rev-parse", "--show-toplevel"],
            BuildGitCliSafeDirectories(path));
        if (result.ExitCode != 0)
        {
            reason = $"git rev-parse --show-toplevel failed with exit {result.ExitCode}: {result.Error}";
            return false;
        }

        topLevel = result.Output.Trim();
        if (!LocalPathEquals(topLevel, path))
        {
            reason = $"git CLI workdir root '{topLevel}' does not equal requested path '{path}'";
            return false;
        }

        reason = "exact Git workdir via git CLI";
        return true;
    }

    private async Task<GitCliBranchHead?> GetGitCliBranchHeadCommitAsync(
        string sourcePath,
        string branchName,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? safeDirectories = null)
    {
        safeDirectories ??= BuildGitCliSafeDirectories(sourcePath);

        var localResult = await RunGitCliAsync(
            sourcePath,
            ["rev-parse", "--verify", $"refs/heads/{branchName}^{{commit}}"],
            cancellationToken,
            throwOnError: false,
            safeDirectories: safeDirectories);
        if (localResult.ExitCode == 0)
        {
            return new GitCliBranchHead(
                localResult.Output.Trim(),
                $"refs/heads/{branchName}",
                $"+refs/heads/{branchName}:refs/remotes/origin/{branchName}");
        }

        var remoteResult = await RunGitCliAsync(
            sourcePath,
            ["rev-parse", "--verify", $"refs/remotes/origin/{branchName}^{{commit}}"],
            cancellationToken,
            throwOnError: false,
            safeDirectories: safeDirectories);
        if (remoteResult.ExitCode == 0)
        {
            return new GitCliBranchHead(
                remoteResult.Output.Trim(),
                $"refs/remotes/origin/{branchName}",
                $"+refs/remotes/origin/{branchName}:refs/remotes/origin/{branchName}");
        }

        var matchingRemoteResult = await RunGitCliAsync(
            sourcePath,
            ["rev-parse", "--verify", $"refs/remotes/{branchName}^{{commit}}"],
            cancellationToken,
            throwOnError: false,
            safeDirectories: safeDirectories);
        if (matchingRemoteResult.ExitCode == 0)
        {
            return new GitCliBranchHead(
                matchingRemoteResult.Output.Trim(),
                $"refs/remotes/{branchName}",
                $"+refs/remotes/{branchName}:refs/remotes/origin/{branchName}");
        }

        _logger.LogWarning(
            "Unable to resolve local git source branch via git CLI. SourcePath: {SourcePath}, Branch: {Branch}, LocalError: {LocalError}, OriginRemoteError: {OriginRemoteError}, MatchingRemoteError: {MatchingRemoteError}",
            sourcePath, branchName, localResult.Error, remoteResult.Error, matchingRemoteResult.Error);
        return null;
    }

    private async Task FetchGitCliBranchAsync(
        string workspacePath,
        string sourceUploadPackArgument,
        GitCliBranchHead targetBranch,
        IReadOnlyList<string> safeDirectories,
        CancellationToken cancellationToken)
    {
        await RunGitCliAsync(
            workspacePath,
            ["fetch", sourceUploadPackArgument, "origin", targetBranch.FetchRefSpec],
            cancellationToken,
            throwOnError: true,
            safeDirectories: safeDirectories);
    }

    private async Task EnsureGitCliWorkspaceHasCommitAsync(
        string workspacePath,
        string targetCommit,
        IReadOnlyList<string> safeDirectories,
        CancellationToken cancellationToken,
        string fetchRefSpec)
    {
        var result = await RunGitCliAsync(
            workspacePath,
            ["cat-file", "-t", targetCommit],
            cancellationToken,
            throwOnError: false,
            safeDirectories: safeDirectories);
        if (result.ExitCode == 0 &&
            string.Equals(result.Output.Trim(), "commit", StringComparison.Ordinal))
        {
            return;
        }

        var showRef = await RunGitCliAsync(
            workspacePath,
            ["show-ref"],
            cancellationToken,
            throwOnError: false,
            safeDirectories: safeDirectories);

        throw new InvalidOperationException(
            $"Fetched local git source ref but target commit is not available in workspace. " +
            $"Workspace: {workspacePath}, Commit: {targetCommit}, FetchRefSpec: {fetchRefSpec}, " +
            $"CatFileExitCode: {result.ExitCode}, CatFileOutput: {result.Output}, CatFileError: {result.Error}, " +
            $"ShowRef: {showRef.Output}");
    }

    private async Task<bool> IsValidGitCliWorkspaceForSourceAsync(
        string workspacePath,
        string sourcePath,
        IReadOnlyList<string> safeDirectories,
        CancellationToken cancellationToken)
    {
        return string.Equals(
            await GetGitCliWorkspaceInvalidReasonAsync(workspacePath, sourcePath, safeDirectories, cancellationToken),
            "workspace is an exact Git workdir and origin matches source",
            StringComparison.Ordinal);
    }

    private async Task<string> GetGitCliWorkspaceInvalidReasonAsync(
        string workspacePath,
        string sourcePath,
        IReadOnlyList<string> safeDirectories,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workspacePath))
        {
            return $"workspace path does not exist: {workspacePath}";
        }

        var topLevelResult = await RunGitCliAsync(
            workspacePath,
            ["rev-parse", "--show-toplevel"],
            cancellationToken,
            throwOnError: false,
            safeDirectories: safeDirectories);
        if (topLevelResult.ExitCode != 0)
        {
            return $"git rev-parse --show-toplevel failed with exit {topLevelResult.ExitCode}: {topLevelResult.Error}";
        }

        var topLevel = topLevelResult.Output.Trim();
        if (!LocalPathEquals(topLevel, workspacePath))
        {
            return $"workspace Git root '{topLevel}' does not equal target path '{workspacePath}'";
        }

        var originResult = await RunGitCliAsync(
            workspacePath,
            ["remote", "get-url", "origin"],
            cancellationToken,
            throwOnError: false,
            safeDirectories: safeDirectories);
        if (originResult.ExitCode != 0)
        {
            return $"git remote get-url origin failed with exit {originResult.ExitCode}: {originResult.Error}";
        }

        var originUrl = originResult.Output.Trim();
        if (!LocalPathEquals(originUrl, sourcePath))
        {
            return $"workspace origin '{originUrl}' does not match source '{sourcePath}'";
        }

        return "workspace is an exact Git workdir and origin matches source";
    }

    private GitCliResult RunGitCli(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string>? safeDirectories = null)
    {
        safeDirectories ??= [];
        LogGitCliCommand(workingDirectory, arguments, safeDirectories);

        try
        {
            using var process = StartGitCli(workingDirectory, arguments, safeDirectories);
            if (process == null)
            {
                return new GitCliResult(-1, string.Empty, "Failed to start git process");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new GitCliResult(process.ExitCode, output.Trim(), error.Trim());
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            return new GitCliResult(-1, string.Empty, ex.Message);
        }
    }

    private async Task<GitCliResult> RunGitCliAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError,
        IReadOnlyList<string>? safeDirectories = null)
    {
        safeDirectories ??= [];
        LogGitCliCommand(workingDirectory, arguments, safeDirectories);

        using var process = StartGitCli(workingDirectory, arguments, safeDirectories);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start git process");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var result = new GitCliResult(
            process.ExitCode,
            (await outputTask).Trim(),
            (await errorTask).Trim());

        if (throwOnError && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed in {workingDirectory} with exit {result.ExitCode}: {result.Error}");
        }

        return result;
    }

    private void LogGitCliCommand(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> safeDirectories)
    {
        _logger.LogInformation(
            "Running git command. WorkingDirectory: {WorkingDirectory}, Arguments: {Arguments}, FullArguments: {FullArguments}, SafeDirectories: {SafeDirectories}",
            workingDirectory,
            string.Join(' ', arguments),
            string.Join(' ', BuildGitCliProcessArguments(arguments, safeDirectories).Select(QuoteGitCliArgumentForLog)),
            string.Join(", ", safeDirectories));
    }

    private static Process? StartGitCli(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> safeDirectories)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in BuildGitCliProcessArguments(arguments, safeDirectories))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo);
    }

    private static IEnumerable<string> BuildGitCliProcessArguments(
        IReadOnlyList<string> arguments,
        IReadOnlyList<string> safeDirectories)
    {
        foreach (var safeDirectory in safeDirectories.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            yield return "-c";
            yield return $"safe.directory={safeDirectory}";
        }

        foreach (var argument in arguments)
        {
            yield return argument;
        }
    }

    private static string BuildGitCliUploadPackArgument(IReadOnlyList<string> safeDirectories)
    {
        var uploadPackArguments = new List<string> { "git" };
        foreach (var safeDirectory in safeDirectories.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            uploadPackArguments.Add("-c");
            uploadPackArguments.Add($"safe.directory={safeDirectory}");
        }

        uploadPackArguments.Add("upload-pack");
        return $"--upload-pack={string.Join(' ', uploadPackArguments.Select(QuoteGitCliArgumentForShell))}";
    }

    private static string QuoteGitCliArgumentForLog(string argument)
    {
        return argument.Any(char.IsWhiteSpace)
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
    }

    private static string QuoteGitCliArgumentForShell(string argument)
    {
        if (OperatingSystem.IsWindows())
        {
            return argument.Any(char.IsWhiteSpace)
                ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                : argument;
        }

        return argument.Contains('\'', StringComparison.Ordinal) || argument.Any(char.IsWhiteSpace)
            ? $"'{argument.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'"
            : argument;
    }

    private static IReadOnlyList<string> BuildGitCliSafeDirectories(params string[] paths)
    {
        var safeDirectories = new List<string>();
        var seen = new HashSet<string>(GetPathComparison() == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

        foreach (var path in paths)
        {
            AddSafeDirectory(safeDirectories, seen, path);

            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalizedPath = NormalizeLocalPath(path);
            var gitPath = Path.Combine(normalizedPath, ".git");
            if (File.Exists(gitPath) || Directory.Exists(gitPath))
            {
                AddSafeDirectory(safeDirectories, seen, gitPath);
            }

            if (TryResolveGitDirPointer(gitPath, normalizedPath, out var gitDir))
            {
                AddSafeDirectory(safeDirectories, seen, gitDir);

                if (TryResolveGitCommonDir(gitDir, out var commonGitDir))
                {
                    AddSafeDirectory(safeDirectories, seen, commonGitDir);
                }
            }
        }

        return safeDirectories;
    }

    private static void AddSafeDirectory(
        List<string> safeDirectories,
        HashSet<string> seen,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalizedPath = NormalizeLocalPath(path);
        if (seen.Add(normalizedPath))
        {
            safeDirectories.Add(normalizedPath);
        }
    }

    private static bool TryResolveGitDirPointer(
        string gitPath,
        string worktreePath,
        out string gitDir)
    {
        gitDir = string.Empty;

        if (!File.Exists(gitPath))
        {
            return false;
        }

        var firstLine = File.ReadLines(gitPath).FirstOrDefault();
        if (firstLine == null ||
            !firstLine.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = firstLine["gitdir:".Length..].Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        gitDir = Path.IsPathRooted(value)
            ? NormalizeLocalPath(value)
            : NormalizeLocalPath(Path.Combine(worktreePath, value));
        return true;
    }

    private static bool TryResolveGitCommonDir(string gitDir, out string commonGitDir)
    {
        commonGitDir = string.Empty;
        var commonDirPath = Path.Combine(gitDir, "commondir");
        if (!File.Exists(commonDirPath))
        {
            return false;
        }

        var value = File.ReadLines(commonDirPath).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        commonGitDir = Path.IsPathRooted(value)
            ? NormalizeLocalPath(value)
            : NormalizeLocalPath(Path.Combine(gitDir, value));
        return true;
    }

    private void DeleteWorkspaceDirectoryWithinRepositoryRoot(string workspacePath, string sourcePath)
    {
        if (!Directory.Exists(workspacePath))
        {
            return;
        }

        var normalizedWorkspace = NormalizeLocalPath(workspacePath);
        var normalizedSource = NormalizeLocalPath(sourcePath);
        if (PathsEqual(normalizedWorkspace, normalizedSource))
        {
            throw new InvalidOperationException(
                $"Refusing to delete local git source while preparing workspace: {workspacePath}");
        }

        var normalizedRoot = NormalizeLocalPath(_options.RepositoriesDirectory);
        if (PathsEqual(normalizedWorkspace, normalizedRoot) ||
            !IsPathUnder(normalizedWorkspace, normalizedRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to delete workspace outside repository root. Workspace: {workspacePath}, Root: {_options.RepositoriesDirectory}");
        }

        DeleteDirectoryRecursive(workspacePath);
    }

    private static bool LocalPathEquals(string pathA, string pathB)
    {
        return PathsEqual(NormalizeLocalPath(pathA), NormalizeLocalPath(pathB));
    }

    private static bool PathsEqual(string normalizedPathA, string normalizedPathB)
    {
        return string.Equals(normalizedPathA, normalizedPathB, GetPathComparison());
    }

    private static bool IsPathUnder(string normalizedPath, string normalizedParent)
    {
        var parentWithSeparator = normalizedParent.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(parentWithSeparator, GetPathComparison());
    }

    private static string NormalizeLocalPath(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            path = uri.LocalPath;
        }

        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private readonly record struct LocalGitSource(string RepositoryPath, LocalGitAccessMode AccessMode);

    private readonly record struct GitCliBranchHead(string CommitId, string SourceRef, string FetchRefSpec);

    private readonly record struct GitCliResult(int ExitCode, string Output, string Error);

    private enum LocalGitAccessMode
    {
        LibGit2,
        GitCli
    }

    private static string ComputeDirectorySnapshotId(string directoryPath)
    {
        using var sha = SHA256.Create();
        var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            var relativePathBytes = System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(directoryPath, file));
            sha.TransformBlock(relativePathBytes, 0, relativePathBytes.Length, null, 0);

            var metadata = BitConverter.GetBytes(info.Length)
                .Concat(BitConverter.GetBytes(info.LastWriteTimeUtc.Ticks))
                .ToArray();
            sha.TransformBlock(metadata, 0, metadata.Length, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static async Task<bool> TryCreateDirectoryLinkAsync(
        string linkPath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return false;
                }

                await process.WaitForExitAsync(cancellationToken);
                return process.ExitCode == 0 && Directory.Exists(linkPath);
            }

            var psi = new ProcessStartInfo
            {
                FileName = "ln",
                Arguments = $"-s \"{targetPath}\" \"{linkPath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var unixProcess = Process.Start(psi);
            if (unixProcess == null)
            {
                return false;
            }

            await unixProcess.WaitForExitAsync(cancellationToken);
            return unixProcess.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sanitizes a path component to prevent directory traversal attacks.
    /// </summary>
    private static string SanitizePathComponent(string component)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            throw new ArgumentException("Path component cannot be empty.", nameof(component));
        }

        // Remove any path separators and dangerous characters
        var sanitized = component
            .Replace('/', '_')
            .Replace('\\', '_')
            .Replace("..", "_")
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ArgumentException("Path component is invalid after sanitization.", nameof(component));
        }

        return sanitized;
    }

    /// <summary>
    /// Builds LibGit2Sharp credentials from repository authentication info.
    /// </summary>
    private static Credentials? BuildCredentials(Entities.Repository repository)
    {
        if (string.IsNullOrWhiteSpace(repository.AuthAccount) && 
            string.IsNullOrWhiteSpace(repository.AuthPassword))
        {
            return null;
        }

        return new UsernamePasswordCredentials
        {
            Username = repository.AuthAccount ?? string.Empty,
            Password = repository.AuthPassword ?? string.Empty
        };
    }


    /// <summary>
    /// Clones a repository to the working directory.
    /// </summary>
    private async Task CloneRepositoryAsync(
        RepositoryWorkspace workspace,
        Credentials? credentials,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Starting repository clone. GitUrl: {Url}, Branch: {Branch}, TargetPath: {Path}",
            workspace.GitUrl, workspace.BranchName, workspace.WorkingDirectory);

        // Clean up any existing partial clone
        if (Directory.Exists(workspace.WorkingDirectory))
        {
            _logger.LogDebug("Removing existing partial clone at {Path}", workspace.WorkingDirectory);
            DeleteDirectoryRecursive(workspace.WorkingDirectory);
        }

        var cloneOptions = new CloneOptions
        {
            BranchName = workspace.BranchName,
            RecurseSubmodules = false
        };

        // 跳过 SSL 证书验证（解决 TLS 解密错误）
        cloneOptions.FetchOptions.CertificateCheck = (_, _, _) => true;

        if (credentials != null)
        {
            cloneOptions.FetchOptions.CredentialsProvider = (_, _, _) => credentials;
        }

        var retryCount = 0;
        Exception? lastException = null;

        while (retryCount < _options.MaxRetryAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogDebug(
                    "Clone attempt {Attempt}/{MaxAttempts}. GitUrl: {Url}",
                    retryCount + 1, _options.MaxRetryAttempts, workspace.GitUrl);

                await Task.Run(() =>
                {
                    GitRepository.Clone(workspace.GitUrl, workspace.WorkingDirectory, cloneOptions);
                    
                    using var repo = new GitRepository(workspace.WorkingDirectory);
                    CheckoutRemoteBranchHard(repo, workspace.BranchName);
                }, cancellationToken);

                stopwatch.Stop();
                _logger.LogInformation(
                    "Repository cloned successfully. GitUrl: {Url}, Branch: {Branch}, TargetPath: {Path}, Duration: {Duration}ms",
                    workspace.GitUrl, workspace.BranchName, workspace.WorkingDirectory, stopwatch.ElapsedMilliseconds);
                return;
            }
            catch (LibGit2SharpException ex)
            {
                lastException = ex;
                retryCount++;

                _logger.LogWarning(
                    ex,
                    "Clone attempt {Attempt}/{MaxAttempts} failed. GitUrl: {Url}, ErrorMessage: {ErrorMessage}",
                    retryCount, _options.MaxRetryAttempts, workspace.GitUrl, ex.Message);

                if (retryCount < _options.MaxRetryAttempts)
                {
                    _logger.LogInformation(
                        "Retrying clone in {Delay}ms. GitUrl: {Url}",
                        _options.RetryDelayMs, workspace.GitUrl);

                    await Task.Delay(_options.RetryDelayMs, cancellationToken);
                }
            }
        }

        stopwatch.Stop();
        _logger.LogError(
            lastException,
            "Repository clone failed after all retry attempts. GitUrl: {Url}, Attempts: {Attempts}, Duration: {Duration}ms",
            workspace.GitUrl, _options.MaxRetryAttempts, stopwatch.ElapsedMilliseconds);

        throw new InvalidOperationException(
            $"Failed to clone repository after {_options.MaxRetryAttempts} attempts: {workspace.GitUrl}",
            lastException);
    }

    /// <summary>
    /// Pulls latest changes from the remote repository.
    /// </summary>
    private async Task PullRepositoryAsync(
        RepositoryWorkspace workspace,
        Credentials? credentials,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Starting repository pull. Path: {Path}, Branch: {Branch}",
            workspace.WorkingDirectory, workspace.BranchName);

        var retryCount = 0;
        Exception? lastException = null;

        while (retryCount < _options.MaxRetryAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogDebug(
                    "Pull attempt {Attempt}/{MaxAttempts}. Path: {Path}",
                    retryCount + 1, _options.MaxRetryAttempts, workspace.WorkingDirectory);

                await Task.Run(() =>
                {
                    using var repo = new GitRepository(workspace.WorkingDirectory);

                    // Fetch from remote
                    var remote = repo.Network.Remotes["origin"];
                    var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);

                    var fetchOptions = new FetchOptions();
                    // 跳过 SSL 证书验证（解决 TLS 解密错误）
                    fetchOptions.CertificateCheck = (_, _, _) => true;
                    
                    if (credentials != null)
                    {
                        fetchOptions.CredentialsProvider = (_, _, _) => credentials;
                    }

                    _logger.LogDebug("Fetching from remote 'origin'");
                    Commands.Fetch(repo, remote.Name, refSpecs, fetchOptions, null);

                    CheckoutRemoteBranchHard(repo, workspace.BranchName);
                }, cancellationToken);

                stopwatch.Stop();
                _logger.LogInformation(
                    "Repository pulled successfully. Path: {Path}, Branch: {Branch}, Duration: {Duration}ms",
                    workspace.WorkingDirectory, workspace.BranchName, stopwatch.ElapsedMilliseconds);
                return;
            }
            catch (LibGit2SharpException ex)
            {
                lastException = ex;
                retryCount++;

                _logger.LogWarning(
                    ex,
                    "Pull attempt {Attempt}/{MaxAttempts} failed. Path: {Path}, ErrorMessage: {ErrorMessage}",
                    retryCount, _options.MaxRetryAttempts, workspace.WorkingDirectory, ex.Message);

                if (retryCount < _options.MaxRetryAttempts)
                {
                    _logger.LogInformation(
                        "Retrying pull in {Delay}ms. Path: {Path}",
                        _options.RetryDelayMs, workspace.WorkingDirectory);

                    await Task.Delay(_options.RetryDelayMs, cancellationToken);
                }
            }
        }

        stopwatch.Stop();
        _logger.LogError(
            lastException,
            "Repository pull failed after all retry attempts. Path: {Path}, Attempts: {Attempts}, Duration: {Duration}ms",
            workspace.WorkingDirectory, _options.MaxRetryAttempts, stopwatch.ElapsedMilliseconds);

        throw new InvalidOperationException(
            $"Failed to pull repository after {_options.MaxRetryAttempts} attempts",
            lastException);
    }

    private void CheckoutRemoteBranchHard(GitRepository repo, string branchName)
    {
        var remoteBranch = repo.Branches[$"origin/{branchName}"];
        if (remoteBranch is null)
        {
            throw new InvalidOperationException($"Remote branch 'origin/{branchName}' not found");
        }

        var localBranch = repo.Branches[branchName];
        if (localBranch is null)
        {
            _logger.LogDebug("Creating local branch from remote branch. Branch: {BranchName}", branchName);
            localBranch = repo.CreateBranch(branchName, remoteBranch.Tip);
        }
        else if (localBranch.Tip?.Sha != remoteBranch.Tip.Sha)
        {
            _logger.LogDebug(
                "Local branch differs from remote tip; hard reset will align it. Branch: {BranchName}, LocalTip: {LocalTip}, RemoteTip: {RemoteTip}",
                branchName,
                localBranch.Tip?.Sha ?? "none",
                remoteBranch.Tip.Sha);
        }

        repo.Branches.Update(localBranch, b => b.TrackedBranch = remoteBranch.CanonicalName);
        Commands.Checkout(repo, localBranch);
        repo.Reset(ResetMode.Hard, remoteBranch.Tip);

        _logger.LogDebug(
            "Checked out branch at remote tip. Branch: {BranchName}, Commit: {CommitId}",
            branchName,
            remoteBranch.Tip.Sha);
    }


    /// <summary>
    /// Gets the HEAD commit ID of a repository.
    /// </summary>
    private string GetHeadCommitId(string workingDirectory)
    {
        using var repo = new GitRepository(workingDirectory);
        return repo.Head.Tip.Sha;
    }

    /// <summary>
    /// Gets all tracked files in the repository.
    /// </summary>
    private string[] GetAllTrackedFiles(string workingDirectory)
    {
        using var repo = new GitRepository(workingDirectory);
        
        var files = new List<string>();
        var tree = repo.Head.Tip.Tree;

        CollectFilesFromTree(tree, string.Empty, files);

        return files.ToArray();
    }

    /// <summary>
    /// Recursively collects file paths from a Git tree.
    /// </summary>
    private static void CollectFilesFromTree(Tree tree, string basePath, List<string> files)
    {
        foreach (var entry in tree)
        {
            var path = string.IsNullOrEmpty(basePath) 
                ? entry.Name 
                : $"{basePath}/{entry.Name}";

            if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                files.Add(path);
            }
            else if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                CollectFilesFromTree((Tree)entry.Target, path, files);
            }
        }
    }

    /// <summary>
    /// Gets files changed between two commits.
    /// </summary>
    private string[] GetChangedFilesBetweenCommits(
        string workingDirectory,
        string fromCommitId,
        string toCommitId)
    {
        using var repo = new GitRepository(workingDirectory);

        var fromCommit = repo.Lookup<Commit>(fromCommitId);
        var toCommit = repo.Lookup<Commit>(toCommitId);

        if (fromCommit == null)
        {
            _logger.LogWarning("From commit {CommitId} not found, returning all files", fromCommitId);
            return GetAllTrackedFiles(workingDirectory);
        }

        if (toCommit == null)
        {
            throw new InvalidOperationException($"To commit {toCommitId} not found");
        }

        var changes = repo.Diff.Compare<TreeChanges>(fromCommit.Tree, toCommit.Tree);

        var changedFiles = new List<string>();

        foreach (var change in changes)
        {
            // Include added, modified, and renamed files
            switch (change.Status)
            {
                case ChangeKind.Added:
                case ChangeKind.Modified:
                case ChangeKind.Renamed:
                case ChangeKind.Copied:
                    changedFiles.Add(change.Path);
                    break;
                case ChangeKind.Deleted:
                    // We might want to track deleted files separately
                    // For now, we don't include them in the changed files list
                    break;
            }
        }

        return changedFiles.ToArray();
    }

    /// <inheritdoc />
    public Task<string?> DetectPrimaryLanguageAsync(
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Detecting primary language. Repository: {Org}/{Repo}, Path: {Path}",
            workspace.Organization, workspace.RepositoryName, workspace.WorkingDirectory);

        var languageStats = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var files = Directory.GetFiles(workspace.WorkingDirectory, "*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 跳过隐藏目录和常见的非代码目录
                var relativePath = Path.GetRelativePath(workspace.WorkingDirectory, file);
                if (ShouldSkipPath(relativePath))
                    continue;

                var extension = Path.GetExtension(file).ToLowerInvariant();
                var language = GetLanguageFromExtension(extension);

                if (language != null)
                {
                    var fileInfo = new FileInfo(file);
                    if (languageStats.ContainsKey(language))
                        languageStats[language] += fileInfo.Length;
                    else
                        languageStats[language] = fileInfo.Length;
                }
            }

            string? primaryLanguage = null;
            if (languageStats.Count > 0)
            {
                primaryLanguage = languageStats.OrderByDescending(kv => kv.Value).First().Key;
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "Primary language detected. Repository: {Org}/{Repo}, Language: {Language}, Duration: {Duration}ms",
                workspace.Organization, workspace.RepositoryName, primaryLanguage ?? "unknown", stopwatch.ElapsedMilliseconds);

            if (languageStats.Count > 0)
            {
                var topLanguages = languageStats.OrderByDescending(kv => kv.Value).Take(5)
                    .Select(kv => $"{kv.Key}:{kv.Value / 1024}KB");
                _logger.LogDebug("Language statistics: {Stats}", string.Join(", ", topLanguages));
            }

            return Task.FromResult(primaryLanguage);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex,
                "Failed to detect primary language. Repository: {Org}/{Repo}, Duration: {Duration}ms",
                workspace.Organization, workspace.RepositoryName, stopwatch.ElapsedMilliseconds);
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// Determines if a path should be skipped during language detection.
    /// </summary>
    private static bool ShouldSkipPath(string relativePath)
    {
        var skipPatterns = new[]
        {
            ".git", "node_modules", "vendor", "bin", "obj", "dist", "build",
            ".vs", ".idea", ".vscode", "__pycache__", ".next", "packages"
        };

        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => skipPatterns.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Maps file extensions to programming language names.
    /// </summary>
    private static string? GetLanguageFromExtension(string extension)
    {
        return extension switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".py" => "Python",
            ".js" => "JavaScript",
            ".ts" => "TypeScript",
            ".tsx" => "TypeScript",
            ".jsx" => "JavaScript",
            ".go" => "Go",
            ".rs" => "Rust",
            ".rb" => "Ruby",
            ".php" => "PHP",
            ".swift" => "Swift",
            ".kt" => "Kotlin",
            ".kts" => "Kotlin",
            ".scala" => "Scala",
            ".c" => "C",
            ".h" => "C",
            ".cpp" => "C++",
            ".cc" => "C++",
            ".cxx" => "C++",
            ".hpp" => "C++",
            ".m" => "Objective-C",
            ".mm" => "Objective-C",
            ".lua" => "Lua",
            ".pl" => "Perl",
            ".pm" => "Perl",
            ".r" => "R",
            ".dart" => "Dart",
            ".ex" => "Elixir",
            ".exs" => "Elixir",
            ".erl" => "Erlang",
            ".hrl" => "Erlang",
            ".hs" => "Haskell",
            ".fs" => "F#",
            ".fsx" => "F#",
            ".clj" => "Clojure",
            ".cljs" => "Clojure",
            ".vue" => "Vue",
            ".svelte" => "Svelte",
            ".sh" => "Shell",
            ".bash" => "Shell",
            ".zsh" => "Shell",
            ".ps1" => "PowerShell",
            ".sql" => "SQL",
            ".groovy" => "Groovy",
            ".gradle" => "Groovy",
            ".zig" => "Zig",
            ".nim" => "Nim",
            ".v" => "V",
            ".jl" => "Julia",
            _ => null
        };
    }

    /// <summary>
    /// Recursively deletes a directory, handling read-only files.
    /// </summary>
    private static void DeleteDirectoryRecursive(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var rootAttributes = File.GetAttributes(path);
        if ((rootAttributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
        {
            Directory.Delete(path, false);
            return;
        }

        // Remove read-only attributes from all files
        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        // Delete the directory
        Directory.Delete(path, true);
    }
}
