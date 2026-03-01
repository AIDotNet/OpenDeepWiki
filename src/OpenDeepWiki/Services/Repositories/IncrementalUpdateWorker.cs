using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Repositories;

/// <summary>
/// 增量更新后台工作器
/// 独立轮询和处理需要增量更新的仓库
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
    /// 执行后台任务
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "IncrementalUpdateWorker started. PollingInterval: {PollingInterval}s",
            _options.PollingIntervalSeconds);

        // 等待应用启动完成
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingTasksAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 正常关闭，不记录错误
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during incremental update polling");
            }

            // 等待下一次轮询
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
    /// 处理待处理的任务
    /// </summary>
    private async Task ProcessPendingTasksAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var updateService = scope.ServiceProvider.GetRequiredService<IIncrementalUpdateService>();
        var gitPlatformService = scope.ServiceProvider.GetRequiredService<IGitPlatformService>();

        // 1. 优先处理高优先级任务（手动触发）
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

        // 2. 检查需要定期更新的仓库
        await CheckScheduledUpdatesAsync(context, gitPlatformService, stoppingToken);
    }

    /// <summary>
    /// 获取待处理的任务（按优先级排序）
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
    /// 处理单个任务
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
            // 更新状态为 Processing
            await UpdateTaskStatusAsync(
                context, task, IncrementalUpdateStatus.Processing, null, stoppingToken);

            // 执行增量更新
            var result = await updateService.ProcessIncrementalUpdateAsync(
                task.RepositoryId, task.BranchId, stoppingToken);

            if (result.Success)
            {
                // 更新状态为 Completed
                task.TargetCommitId = result.CurrentCommitId;
                await UpdateTaskStatusAsync(
                    context, task, IncrementalUpdateStatus.Completed, null, stoppingToken);

                _logger.LogInformation(
                    "Task completed successfully. TaskId: {TaskId}, ChangedFiles: {ChangedFiles}, Duration: {Duration}ms",
                    task.Id, result.ChangedFilesCount, result.Duration.TotalMilliseconds);
            }
            else
            {
                // 更新状态为 Failed，保留上次 CommitId
                await UpdateTaskStatusAsync(
                    context, task, IncrementalUpdateStatus.Failed, result.ErrorMessage, stoppingToken);

                _logger.LogWarning(
                    "Task failed. TaskId: {TaskId}, Error: {Error}",
                    task.Id, result.ErrorMessage);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 取消时保持任务状态为 Pending，以便下次重试
            _logger.LogInformation(
                "Task processing cancelled. TaskId: {TaskId}", task.Id);
            throw;
        }
        catch (Exception ex)
        {
            // 更新状态为 Failed
            await UpdateTaskStatusAsync(
                context, task, IncrementalUpdateStatus.Failed, ex.Message, stoppingToken);

            _logger.LogError(ex,
                "Task processing failed with exception. TaskId: {TaskId}",
                task.Id);
        }
    }


    /// <summary>
    /// 更新任务状态
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
    /// 检查需要定期更新的仓库
    /// </summary>
    private async Task CheckScheduledUpdatesAsync(
        IContext context,
        IGitPlatformService gitPlatformService,
        CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;

        // 查询需要检查更新的仓库
        // 条件：状态为 Completed 且距离上次检查时间超过配置的间隔
        var repositoriesToCheck = await context.Repositories
            .Where(r => r.Status == RepositoryStatus.Completed)
            .Where(r => r.LastUpdateCheckAt == null ||
                        r.LastUpdateCheckAt.Value.AddMinutes(
                            r.UpdateIntervalMinutes ?? _options.DefaultUpdateIntervalMinutes) <= now)
            .Take(10) // 每次最多检查10个仓库，避免单次处理过多
            .ToListAsync(stoppingToken);

        foreach (var repository in repositoriesToCheck)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // Sync repository visibility before creating update tasks
            await SyncRepositoryVisibilityAsync(context, gitPlatformService, repository);

            await CreateScheduledUpdateTasksAsync(context, repository, stoppingToken);
        }
    }

    /// <summary>
    /// Sync repository visibility with the actual Git platform state
    /// </summary>
    private async Task SyncRepositoryVisibilityAsync(
        IContext context,
        IGitPlatformService gitPlatformService,
        Repository repository)
    {
        try
        {
            if (!IsPublicPlatform(repository.GitUrl) ||
                string.IsNullOrWhiteSpace(repository.OrgName) ||
                string.IsNullOrWhiteSpace(repository.RepoName))
            {
                return;
            }

            var repoInfo = await gitPlatformService.CheckRepoExistsAsync(repository.OrgName, repository.RepoName);
            if (!repoInfo.Exists)
            {
                return;
            }

            var shouldBePublic = !repoInfo.IsPrivate;
            if (repository.IsPublic != shouldBePublic)
            {
                _logger.LogInformation(
                    "Visibility mismatch detected for {Org}/{Repo}: DB={DbVisibility}, Actual={ActualVisibility}. Updating.",
                    repository.OrgName, repository.RepoName,
                    repository.IsPublic ? "Public" : "Private",
                    shouldBePublic ? "Public" : "Private");

                repository.IsPublic = shouldBePublic;
                repository.UpdatedAt = DateTime.UtcNow;
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
    /// Check if the git URL is from a supported public platform
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

    /// <summary>
    /// 为仓库创建定期更新任务
    /// </summary>
    private async Task CreateScheduledUpdateTasksAsync(
        IContext context,
        Repository repository,
        CancellationToken stoppingToken)
    {
        try
        {
            // 获取仓库的所有分支
            var branches = await context.RepositoryBranches
                .Where(b => b.RepositoryId == repository.Id)
                .ToListAsync(stoppingToken);

            foreach (var branch in branches)
            {
                // 检查是否已存在待处理或处理中的任务
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

                // 创建定期更新任务（普通优先级）
                var task = new IncrementalUpdateTask
                {
                    Id = Guid.NewGuid().ToString(),
                    RepositoryId = repository.Id,
                    BranchId = branch.Id,
                    PreviousCommitId = branch.LastCommitId,
                    Status = IncrementalUpdateStatus.Pending,
                    Priority = 0, // 普通优先级
                    IsManualTrigger = false,
                    CreatedAt = DateTime.UtcNow
                };

                context.IncrementalUpdateTasks.Add(task);

                _logger.LogInformation(
                    "Created scheduled update task. TaskId: {TaskId}, Repository: {Org}/{Repo}, Branch: {Branch}",
                    task.Id, repository.OrgName, repository.RepoName, branch.BranchName);
            }

            // 更新仓库的 LastUpdateCheckAt
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
}
