using System.Diagnostics;
using LibGit2Sharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.GitHub;
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
}

/// <summary>
/// Implementation of IRepositoryAnalyzer using LibGit2Sharp.
/// Handles cloning, updating, and analyzing Git repositories.
/// </summary>
public class RepositoryAnalyzer : IRepositoryAnalyzer
{
    private readonly RepositoryAnalyzerOptions _options;
    private readonly ILogger<RepositoryAnalyzer> _logger;
    private readonly IGitHubAppService _gitHubAppService;
    private readonly IContext _context;
    private readonly IConfiguration _configuration;

    public RepositoryAnalyzer(
        IOptions<RepositoryAnalyzerOptions> options,
        ILogger<RepositoryAnalyzer> logger,
        IGitHubAppService gitHubAppService,
        IContext context,
        IConfiguration configuration)
    {
        _options = options.Value;
        _logger = logger;
        _gitHubAppService = gitHubAppService;
        _context = context;
        _configuration = configuration;

        _logger.LogDebug(
            "RepositoryAnalyzer initialized. RepositoriesDirectory: {RepoDir}, CleanupAfterProcessing: {Cleanup}, MaxRetryAttempts: {MaxRetry}",
            _options.RepositoriesDirectory, _options.CleanupAfterProcessing, _options.MaxRetryAttempts);
    }

    /// <inheritdoc />
    public async Task<RepositoryWorkspace> PrepareWorkspaceAsync(
        Entities.Repository repository,
        string branchName,
        string? previousCommitId = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var workspace = new RepositoryWorkspace
        {
            Organization = repository.OrgName,
            RepositoryName = repository.RepoName,
            BranchName = branchName,
            GitUrl = repository.GitUrl,
            PreviousCommitId = previousCommitId,
            WorkingDirectory = GetWorkingDirectory(repository.OrgName, repository.RepoName)
        };

        _logger.LogInformation(
            "Preparing workspace. Repository: {Org}/{Repo}, Branch: {Branch}, WorkingDirectory: {Path}, PreviousCommit: {PreviousCommit}",
            workspace.Organization, workspace.RepositoryName, branchName, 
            workspace.WorkingDirectory, previousCommitId ?? "none");

        // Ensure the parent directory exists
        var parentDir = Path.GetDirectoryName(workspace.WorkingDirectory);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            _logger.LogDebug("Creating parent directory: {ParentDir}", parentDir);
            Directory.CreateDirectory(parentDir);
        }

        // Build credentials: per-repo auth > GitHub App installation token > global GITHUB_TOKEN
        var credentials = await BuildCredentialsAsync(repository, cancellationToken);
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
    /// Format: {RepositoriesDirectory}/{organization}/{name}/tree/
    /// </summary>
    private string GetWorkingDirectory(string organization, string repositoryName)
    {
        // Sanitize organization and repository names to prevent path traversal
        var safeOrg = SanitizePathComponent(organization);
        var safeRepo = SanitizePathComponent(repositoryName);

        return Path.Combine(_options.RepositoriesDirectory, safeOrg, safeRepo, "tree");
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
    /// Builds LibGit2Sharp credentials with fallback chain:
    /// 1. Per-repo AuthAccount/AuthPassword (existing behavior)
    /// 2. GitHub App installation token (if org has an installation)
    /// 3. Global GITHUB_TOKEN environment variable
    /// </summary>
    private async Task<Credentials?> BuildCredentialsAsync(
        Entities.Repository repository,
        CancellationToken cancellationToken = default)
    {
        // Priority 1: Per-repo credentials
        if (!string.IsNullOrWhiteSpace(repository.AuthAccount) ||
            !string.IsNullOrWhiteSpace(repository.AuthPassword))
        {
            _logger.LogDebug("Using per-repo credentials for {Org}/{Repo}", repository.OrgName, repository.RepoName);
            return new UsernamePasswordCredentials
            {
                Username = repository.AuthAccount ?? string.Empty,
                Password = repository.AuthPassword ?? string.Empty
            };
        }

        // Priority 2: GitHub App installation token
        if (_gitHubAppService.IsConfigured && !string.IsNullOrWhiteSpace(repository.OrgName))
        {
            try
            {
                var installation = await _context.GitHubAppInstallations
                    .FirstOrDefaultAsync(i => i.AccountLogin == repository.OrgName && !i.IsDeleted, cancellationToken);

                if (installation != null)
                {
                    var token = await _gitHubAppService.GetInstallationTokenAsync(installation.InstallationId, cancellationToken);
                    _logger.LogDebug("Using GitHub App installation token for {Org}/{Repo}", repository.OrgName, repository.RepoName);
                    return new UsernamePasswordCredentials
                    {
                        Username = "x-access-token",
                        Password = token
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get GitHub App installation token for {Org}/{Repo}, falling back", repository.OrgName, repository.RepoName);
            }
        }

        // Priority 3: Global GITHUB_TOKEN
        var globalToken = _configuration["GitHub:Token"];
        if (!string.IsNullOrWhiteSpace(globalToken))
        {
            _logger.LogDebug("Using global GitHub token for {Org}/{Repo}", repository.OrgName, repository.RepoName);
            return new UsernamePasswordCredentials
            {
                Username = "x-access-token",
                Password = globalToken
            };
        }

        return null;
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
                    
                    // Explicitly checkout the target branch after clone
                    using var repo = new GitRepository(workspace.WorkingDirectory);
                    var targetBranch = repo.Branches[workspace.BranchName] 
                        ?? repo.Branches[$"origin/{workspace.BranchName}"];
                    
                    if (targetBranch != null)
                    {
                        if (targetBranch.IsRemote)
                        {
                            // Create local tracking branch from remote
                            var localBranch = repo.Branches[workspace.BranchName];
                            if (localBranch == null)
                            {
                                localBranch = repo.CreateBranch(workspace.BranchName, targetBranch.Tip);
                                repo.Branches.Update(localBranch, b => b.TrackedBranch = targetBranch.CanonicalName);
                            }
                            targetBranch = localBranch;
                        }
                        Commands.Checkout(repo, targetBranch);
                    }
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

                    // Checkout the target branch
                    var branch = repo.Branches[workspace.BranchName] 
                        ?? repo.Branches[$"origin/{workspace.BranchName}"];

                    if (branch == null)
                    {
                        throw new InvalidOperationException($"Branch '{workspace.BranchName}' not found");
                    }

                    _logger.LogDebug("Found branch: {BranchName}, IsRemote: {IsRemote}", 
                        branch.FriendlyName, branch.IsRemote);

                    // If it's a remote tracking branch, create a local branch
                    if (branch.IsRemote)
                    {
                        var localBranch = repo.Branches[workspace.BranchName];
                        if (localBranch == null)
                        {
                            _logger.LogDebug("Creating local branch from remote tracking branch");
                            localBranch = repo.CreateBranch(workspace.BranchName, branch.Tip);
                            repo.Branches.Update(localBranch, b => b.TrackedBranch = branch.CanonicalName);
                        }
                        branch = localBranch;
                    }

                    _logger.LogDebug("Checking out branch: {BranchName}", branch.FriendlyName);
                    Commands.Checkout(repo, branch);

                    // Pull (merge) changes
                    var pullOptions = new PullOptions
                    {
                        FetchOptions = fetchOptions,
                        MergeOptions = new MergeOptions
                        {
                            FastForwardStrategy = FastForwardStrategy.Default
                        }
                    };

                    var signature = new Signature("OpenDeepWiki", "wiki@opendeepwiki.local", DateTimeOffset.Now);
                    _logger.LogDebug("Pulling changes");
                    Commands.Pull(repo, signature, pullOptions);

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
