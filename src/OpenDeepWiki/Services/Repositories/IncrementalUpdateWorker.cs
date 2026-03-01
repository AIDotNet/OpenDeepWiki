using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Repositories;

/// <summary>
/// Incremental update background worker
/// Independently polls and processes repositories needing incremental updates
/// </summary>
public class IncrementalUpdateWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IncrementalUpdateWorker> _logger;
    private readonly IncrementalUpdateOptions _options;

    public IncrementalUpdateWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<IncrementalUpdateWorker> logger,
        IOptions<IncrementalUpdateOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Execute background task
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "IncrementalUpdateWorker started. PollingInterval: {PollingInterval}s",
            _options.PollingIntervalSeconds);

        // Wait for application startup to complete
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingTasksAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown, do not log error
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during incremental update polling");
            }

            // Wait for next polling cycle
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.PollingIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("IncrementalUpdateWorker stopped gracefully");
    }


    /// <summary>
    /// Process pending tasks
    /// </summary>
    private async Task ProcessPendingTasksAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var updateService = scope.ServiceProvider.GetRequiredService<IIncrementalUpdateService>();
        var gitPlatformService = scope.ServiceProvider.GetService<IGitPlatformService>();

        // 1. Process high-priority tasks first (manually triggered)
        var pendingTasks = await GetPendingTasksAsync(context, stoppingToken);

        foreach (var task in pendingTasks)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Cancellation requested, stopping task processing");
                break;
            }

            await ProcessSingleTaskAsync(context, updateService, task, stoppingToken);
        }

        // 2. Check repositories that need scheduled updates
        await CheckScheduledUpdatesAsync(context, gitPlatformService, stoppingToken);
    }

    /// <summary>
    /// Get pending tasks (sorted by priority)
    /// </summary>
    private async Task<List<IncrementalUpdateTask>> GetPendingTasksAsync(
        IContext context,
        CancellationToken stoppingToken)
    {
        return await context.IncrementalUpdateTasks
            .Where(t => t.Status == IncrementalUpdateStatus.Pending)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(stoppingToken);
    }

    /// <summary>
    /// Process a single task
    /// </summary>
    private async Task ProcessSingleTaskAsync(
        IContext context,
        IIncrementalUpdateService updateService,
        IncrementalUpdateTask task,
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Processing task. TaskId: {TaskId}, RepositoryId: {RepositoryId}, BranchId: {BranchId}, Priority: {Priority}",
            task.Id, task.RepositoryId, task.BranchId, task.Priority);

        try
        {
            // Update status to Processing
            await UpdateTaskStatusAsync(
                context, task, IncrementalUpdateStatus.Processing, null, stoppingToken);

            // Execute incremental update
            var result = await updateService.ProcessIncrementalUpdateAsync(
                task.RepositoryId, task.BranchId, stoppingToken);

            if (result.Success)
            {
                // Update status to Completed
                task.TargetCommitId = result.CurrentCommitId;
                await UpdateTaskStatusAsync(
                    context, task, IncrementalUpdateStatus.Completed, null, stoppingToken);

                _logger.LogInformation(
                    "Task completed successfully. TaskId: {TaskId}, ChangedFiles: {ChangedFiles}, Duration: {Duration}ms",
                    task.Id, result.ChangedFilesCount, result.Duration.TotalMilliseconds);
            }
            else
            {
                // Update status to Failed, keep last CommitId
                await UpdateTaskStatusAsync(
                    context, task, IncrementalUpdateStatus.Failed, result.ErrorMessage, stoppingToken);

                _logger.LogWarning(
                    "Task failed. TaskId: {TaskId}, Error: {Error}",
                    task.Id, result.ErrorMessage);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Keep task status as Pending on cancellation, for retry next time
            _logger.LogInformation(
                "Task processing cancelled. TaskId: {TaskId}", task.Id);
            throw;
        }
        catch (Exception ex)
        {
            // Update status to Failed
            await UpdateTaskStatusAsync(
                context, task, IncrementalUpdateStatus.Failed, ex.Message, stoppingToken);

            _logger.LogError(ex,
                "Task processing failed with exception. TaskId: {TaskId}",
                task.Id);
        }
    }


    /// <summary>
    /// Update task status
    /// </summary>
    private async Task UpdateTaskStatusAsync(
        IContext context,
        IncrementalUpdateTask task,
        IncrementalUpdateStatus status,
        string? errorMessage,
        CancellationToken stoppingToken)
    {
        task.Status = status;
        task.ErrorMessage = errorMessage;
        task.UpdatedAt = DateTime.UtcNow;

        switch (status)
        {
            case IncrementalUpdateStatus.Processing:
                task.StartedAt = DateTime.UtcNow;
                break;
            case IncrementalUpdateStatus.Completed:
            case IncrementalUpdateStatus.Failed:
                task.CompletedAt = DateTime.UtcNow;
                if (status == IncrementalUpdateStatus.Failed)
                {
                    task.RetryCount++;
                }
                break;
        }

        await context.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    /// Check repositories that need scheduled updates
    /// </summary>
    private async Task CheckScheduledUpdatesAsync(
        IContext context,
        IGitPlatformService? gitPlatformService,
        CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;

        // Query repositories that need update checks
        // Condition: status is Completed and time since last check exceeds configured interval
        var repositoriesToCheck = await context.Repositories
            .Where(r => r.Status == RepositoryStatus.Completed)
            .Where(r => r.LastUpdateCheckAt == null ||
                        r.LastUpdateCheckAt.Value.AddMinutes(
                            r.UpdateIntervalMinutes ?? _options.DefaultUpdateIntervalMinutes) <= now)
            .Take(10) // Check at most 10 repositories per cycle to avoid processing too many at once
            .ToListAsync(stoppingToken);

        foreach (var repository in repositoriesToCheck)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // Sync visibility from GitHub API
            if (gitPlatformService != null)
            {
                await SyncRepositoryVisibilityAsync(context, gitPlatformService, repository);
            }

            await CreateScheduledUpdateTasksAsync(context, repository, stoppingToken);
        }
    }

    /// <summary>
    /// Create scheduled update tasks for a repository
    /// </summary>
    private async Task CreateScheduledUpdateTasksAsync(
        IContext context,
        Repository repository,
        CancellationToken stoppingToken)
    {
        try
        {
            // Get all branches for the repository
            var branches = await context.RepositoryBranches
                .Where(b => b.RepositoryId == repository.Id)
                .ToListAsync(stoppingToken);

            foreach (var branch in branches)
            {
                // Check if a pending or processing task already exists
                var existingTask = await context.IncrementalUpdateTasks
                    .AnyAsync(t => t.RepositoryId == repository.Id
                                   && t.BranchId == branch.Id
                                   && (t.Status == IncrementalUpdateStatus.Pending
                                       || t.Status == IncrementalUpdateStatus.Processing),
                        stoppingToken);

                if (existingTask)
                {
                    _logger.LogDebug(
                        "Skipping scheduled update, task already exists. Repository: {Org}/{Repo}, Branch: {Branch}",
                        repository.OrgName, repository.RepoName, branch.BranchName);
                    continue;
                }

                // Create scheduled update task (normal priority)
                var task = new IncrementalUpdateTask
                {
                    Id = Guid.NewGuid().ToString(),
                    RepositoryId = repository.Id,
                    BranchId = branch.Id,
                    PreviousCommitId = branch.LastCommitId,
                    Status = IncrementalUpdateStatus.Pending,
                    Priority = 0, // Normal priority
                    IsManualTrigger = false,
                    CreatedAt = DateTime.UtcNow
                };

                context.IncrementalUpdateTasks.Add(task);

                _logger.LogInformation(
                    "Created scheduled update task. TaskId: {TaskId}, Repository: {Org}/{Repo}, Branch: {Branch}",
                    task.Id, repository.OrgName, repository.RepoName, branch.BranchName);
            }

            // Update repository's LastUpdateCheckAt
            repository.LastUpdateCheckAt = DateTime.UtcNow;
            await context.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create scheduled update tasks. Repository: {Org}/{Repo}",
                repository.OrgName, repository.RepoName);
        }
    }

    /// <summary>
    /// Sync repository visibility with the Git platform (e.g. GitHub API)
    /// </summary>
    private async Task SyncRepositoryVisibilityAsync(
        IContext context,
        IGitPlatformService gitPlatformService,
        Repository repository)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(repository.OrgName) || string.IsNullOrWhiteSpace(repository.RepoName))
                return;

            // Only sync for supported platforms (GitHub, GitLab, Gitee)
            if (!IsPublicPlatform(repository.GitUrl))
                return;

            var repoInfo = await gitPlatformService.CheckRepoExistsAsync(repository.OrgName, repository.RepoName);
            if (!repoInfo.Exists)
                return;

            var shouldBePublic = !repoInfo.IsPrivate;
            if (repository.IsPublic != shouldBePublic)
            {
                _logger.LogInformation(
                    "Visibility mismatch detected for {Org}/{Repo}: DB IsPublic={DbIsPublic}, GitHub IsPrivate={GitHubIsPrivate}. Updating to IsPublic={NewIsPublic}",
                    repository.OrgName, repository.RepoName, repository.IsPublic, repoInfo.IsPrivate, shouldBePublic);

                repository.IsPublic = shouldBePublic;
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to sync visibility for {Org}/{Repo}",
                repository.OrgName, repository.RepoName);
        }
    }

    /// <summary>
    /// Check if the platform supports fetching repository statistics
    /// </summary>
    private static bool IsPublicPlatform(string gitUrl)
    {
        try
        {
            var uri = new Uri(gitUrl);
            var host = uri.Host.ToLowerInvariant();
            return host is "github.com" or "gitee.com" or "gitlab.com";
        }
        catch
        {
            return false;
        }
    }
}
