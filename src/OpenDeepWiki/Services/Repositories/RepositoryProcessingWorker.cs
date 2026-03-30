using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Translation;
using OpenDeepWiki.Services.Wiki;
using System.Diagnostics;

namespace OpenDeepWiki.Services.Repositories;

/// <summary>
/// Background worker that processes pending repositories.
/// Polls for pending repositories and generates wiki content using AI.
/// </summary>
public class RepositoryProcessingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<RepositoryProcessingWorker> logger,
    IOptions<WikiGeneratorOptions> wikiOptions) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(30);
    private readonly WikiGeneratorOptions _wikiOptions = wikiOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Repository processing worker started. Polling interval: {PollingInterval}s", 
            PollingInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Repository processing worker is shutting down");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Repository processing loop failed unexpectedly");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }

        logger.LogInformation("Repository processing worker stopped");
    }

    private async Task ProcessPendingAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetService<IContext>();
        var repositoryAnalyzer = scope.ServiceProvider.GetService<IRepositoryAnalyzer>();
        var wikiGenerator = scope.ServiceProvider.GetService<IWikiGenerator>();
        var processingLogService = scope.ServiceProvider.GetService<IProcessingLogService>();

        if (context is null)
        {
            logger.LogWarning("IContext is not registered, skip repository processing");
            return;
        }

        if (repositoryAnalyzer is null)
        {
            logger.LogWarning("IRepositoryAnalyzer is not registered, skip repository processing");
            return;
        }

        if (wikiGenerator is null)
        {
            logger.LogWarning("IWikiGenerator is not registered, skip repository processing");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            // Get the oldest pending repository (ordered by creation time)
            var repository = await context.Repositories
                .OrderBy(item => item.CreatedAt)
                .FirstOrDefaultAsync(item => item.Status == RepositoryStatus.Pending || item.Status == RepositoryStatus.Processing, stoppingToken);

            if (repository is null)
            {
                logger.LogDebug("No pending repositories found");
                break;
            }

            // 清除旧的处理日志
            if (processingLogService != null)
            {
                await processingLogService.ClearLogsAsync(repository.Id, stoppingToken);
            }

            // 设置当前仓库ID到WikiGenerator
            if (wikiGenerator is WikiGenerator generator)
            {
                generator.SetCurrentRepository(repository.Id);
            }

            // Transition to Processing status
            repository.Status = RepositoryStatus.Processing;
            repository.UpdateTimestamp();
            context.Repositories.Update(repository);
            await context.SaveChangesAsync(stoppingToken);

            // 记录开始处理
            if (processingLogService != null)
            {
                await processingLogService.LogAsync(repository.Id, ProcessingStep.Workspace, 
                    $"Starting repository processing: {repository.OrgName}/{repository.RepoName}", cancellationToken: stoppingToken);
            }

            var stopwatch = Stopwatch.StartNew();
            logger.LogInformation(
                "Starting repository processing. RepositoryId: {RepositoryId}, Repository: {Org}/{Repo}, GitUrl: {GitUrl}",
                repository.Id, repository.OrgName, repository.RepoName, repository.GitUrl);

            try
            {
                await ProcessRepositoryAsync(
                    repository, 
                    context, 
                    repositoryAnalyzer, 
                    wikiGenerator,
                    processingLogService,
                    stoppingToken);

                stopwatch.Stop();
                // Transition to Completed status
                repository.Status = RepositoryStatus.Completed;
                logger.LogInformation(
                    "Repository processing completed successfully. RepositoryId: {RepositoryId}, Repository: {Org}/{Repo}, Duration: {Duration}ms",
                    repository.Id, repository.OrgName, repository.RepoName, stopwatch.ElapsedMilliseconds);

                // 记录完成
                if (processingLogService != null)
                {
                    await processingLogService.LogAsync(repository.Id, ProcessingStep.Complete, 
                        $"Repository processing complete, total time: {stopwatch.ElapsedMilliseconds}ms", cancellationToken: stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                repository.Status = RepositoryStatus.Pending; // Reset to pending for retry
                logger.LogWarning(
                    "Repository processing cancelled. RepositoryId: {RepositoryId}, Repository: {Org}/{Repo}, Duration: {Duration}ms",
                    repository.Id, repository.OrgName, repository.RepoName, stopwatch.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                // Transition to Failed status
                repository.Status = RepositoryStatus.Failed;
                logger.LogError(ex, 
                    "Repository processing failed. RepositoryId: {RepositoryId}, Repository: {Org}/{Repo}, Duration: {Duration}ms, ErrorType: {ErrorType}",
                    repository.Id, repository.OrgName, repository.RepoName, stopwatch.ElapsedMilliseconds, ex.GetType().Name);

                // 记录失败
                if (processingLogService != null)
                {
                    await processingLogService.LogAsync(repository.Id, ProcessingStep.Content, 
                        $"Processing failed: {ex.Message}", cancellationToken: stoppingToken);
                }
            }

            repository.UpdateTimestamp();
            context.Repositories.Update(repository);
            await context.SaveChangesAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Processes a single repository: prepares workspace, generates wiki content.
    /// </summary>
    private async Task ProcessRepositoryAsync(
        Repository repository,
        IContext context,
        IRepositoryAnalyzer repositoryAnalyzer,
        IWikiGenerator wikiGenerator,
        IProcessingLogService? processingLogService,
        CancellationToken stoppingToken)
    {
        // Get all branches for this repository
        var branches = await context.RepositoryBranches
            .Where(b => b.RepositoryId == repository.Id)
            .ToListAsync(stoppingToken);

        if (branches.Count == 0)
        {
            logger.LogWarning(
                "No branches found for repository. RepositoryId: {RepositoryId}, Repository: {Org}/{Repo}",
                repository.Id, repository.OrgName, repository.RepoName);
            return;
        }

        logger.LogInformation(
            "Found {BranchCount} branches to process for repository {Org}/{Repo}",
            branches.Count, repository.OrgName, repository.RepoName);

        foreach (var branch in branches)
        {
            stoppingToken.ThrowIfCancellationRequested();

            await ProcessBranchAsync(
                repository,
                branch,
                context,
                repositoryAnalyzer,
                wikiGenerator,
                processingLogService,
                stoppingToken);
        }
    }

    /// <summary>
    /// Processes a single branch: prepares workspace, generates wiki for each language.
    /// </summary>
    private async Task ProcessBranchAsync(
        Repository repository,
        RepositoryBranch branch,
        IContext context,
        IRepositoryAnalyzer repositoryAnalyzer,
        IWikiGenerator wikiGenerator,
        IProcessingLogService? processingLogService,
        CancellationToken stoppingToken)
    {
        var branchStopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Starting branch processing. BranchId: {BranchId}, Branch: {BranchName}, Repository: {Org}/{Repo}, LastCommitId: {LastCommitId}",
            branch.Id, branch.BranchName, repository.OrgName, repository.RepoName, branch.LastCommitId ?? "none");

        // 记录准备工作区
        if (processingLogService != null)
        {
            await processingLogService.LogAsync(repository.Id, ProcessingStep.Workspace, 
                $"Preparing workspace, branch: {branch.BranchName}", cancellationToken: stoppingToken);
        }

        // Prepare workspace with previous commit ID for incremental updates
        var workspace = await repositoryAnalyzer.PrepareWorkspaceAsync(
            repository,
            branch.BranchName,
            branch.LastCommitId,
            stoppingToken);

        logger.LogDebug(
            "Workspace prepared. WorkingDirectory: {WorkingDirectory}, CurrentCommit: {CurrentCommit}, PreviousCommit: {PreviousCommit}, IsIncremental: {IsIncremental}",
            workspace.WorkingDirectory, workspace.CommitId, workspace.PreviousCommitId ?? "none", workspace.IsIncremental);

        // 记录工作区准备完成
        if (processingLogService != null)
        {
            await processingLogService.LogAsync(repository.Id, ProcessingStep.Workspace, 
                $"Workspace ready, Commit: {workspace.CommitId[..Math.Min(7, workspace.CommitId.Length)]}", cancellationToken: stoppingToken);
        }

        // 检测并更新仓库主要编程语言（仅在首次处理或语言为空时）
        if (string.IsNullOrEmpty(repository.PrimaryLanguage))
        {
            var detectedLanguage = await repositoryAnalyzer.DetectPrimaryLanguageAsync(workspace, stoppingToken);
            if (!string.IsNullOrEmpty(detectedLanguage))
            {
                repository.PrimaryLanguage = detectedLanguage;
                context.Repositories.Update(repository);
                await context.SaveChangesAsync(stoppingToken);

                logger.LogInformation(
                    "Repository primary language updated. RepositoryId: {RepositoryId}, Language: {Language}",
                    repository.Id, detectedLanguage);

                if (processingLogService != null)
                {
                    await processingLogService.LogAsync(repository.Id, ProcessingStep.Workspace,
                        $"Primary language detected: {detectedLanguage}", cancellationToken: stoppingToken);
                }
            }
        }

        try
        {
            // Get all languages for this branch
            var languages = await context.BranchLanguages
                .Where(l => l.RepositoryBranchId == branch.Id)
                .ToListAsync(stoppingToken);

            if (languages.Count == 0)
            {
                logger.LogWarning(
                    "No languages found for branch. BranchId: {BranchId}, Branch: {BranchName}",
                    branch.Id, branch.BranchName);
                return;
            }

            logger.LogInformation(
                "Found {LanguageCount} languages to process for branch {BranchName}: {Languages}",
                languages.Count, branch.BranchName, string.Join(", ", languages.Select(l => l.LanguageCode)));

            // Check if this is an incremental update
            var isIncremental = workspace.IsIncremental && 
                                workspace.PreviousCommitId != workspace.CommitId;

            string[]? changedFiles = null;
            if (isIncremental)
            {
                changedFiles = await repositoryAnalyzer.GetChangedFilesAsync(
                    workspace,
                    workspace.PreviousCommitId,
                    workspace.CommitId,
                    stoppingToken);

                logger.LogInformation(
                    "Incremental update detected. ChangedFileCount: {Count}, OldCommit: {OldCommit}, NewCommit: {NewCommit}",
                    changedFiles.Length,
                    workspace.PreviousCommitId,
                    workspace.CommitId);

                if (changedFiles.Length > 0 && changedFiles.Length <= 20)
                {
                    logger.LogDebug("Changed files: {ChangedFiles}", string.Join(", ", changedFiles));
                }
            }

            foreach (var language in languages)
            {
                stoppingToken.ThrowIfCancellationRequested();

                await ProcessLanguageAsync(
                    workspace,
                    language,
                    wikiGenerator,
                    context,
                    isIncremental,
                    changedFiles,
                    stoppingToken);
            }

            // Update branch with new commit ID after successful processing
            branch.LastCommitId = workspace.CommitId;
            branch.LastProcessedAt = DateTime.UtcNow;
            context.RepositoryBranches.Update(branch);
            await context.SaveChangesAsync(stoppingToken);

            branchStopwatch.Stop();
            logger.LogInformation(
                "Branch processing completed. BranchId: {BranchId}, Branch: {BranchName}, CommitId: {CommitId}, Duration: {Duration}ms",
                branch.Id, branch.BranchName, workspace.CommitId, branchStopwatch.ElapsedMilliseconds);
        }
        finally
        {
            // Cleanup workspace
            logger.LogDebug("Cleaning up workspace at {WorkingDirectory}", workspace.WorkingDirectory);
            await repositoryAnalyzer.CleanupWorkspaceAsync(workspace, stoppingToken);
        }
    }

    /// <summary>
    /// Processes a single language: generates or updates wiki content.
    /// </summary>
    private async Task ProcessLanguageAsync(
        RepositoryWorkspace workspace,
        BranchLanguage language,
        IWikiGenerator wikiGenerator,
        IContext context,
        bool isIncremental,
        string[]? changedFiles,
        CancellationToken stoppingToken)
    {
        var languageStopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Starting language processing. LanguageId: {LanguageId}, Language: {LanguageCode}, Repository: {Org}/{Repo}, Mode: {Mode}",
            language.Id, language.LanguageCode, workspace.Organization, workspace.RepositoryName,
            isIncremental ? "Incremental" : "Full");

        try
        {
            if (isIncremental && changedFiles != null && changedFiles.Length > 0)
            {
                // Incremental update: only update affected documents
                logger.LogDebug("Performing incremental update for {LanguageCode} with {FileCount} changed files",
                    language.LanguageCode, changedFiles.Length);
                    
                await wikiGenerator.IncrementalUpdateAsync(
                    workspace,
                    language,
                    changedFiles,
                    stoppingToken);
            }
            else
            {
                // Full generation: generate catalog and all documents
                // 思维导图由 MindMapWorker 独立后台任务生成
                // 翻译任务由 TranslationWorker 独立后台任务扫描并创建
                logger.LogDebug("Performing full wiki generation for {LanguageCode}", language.LanguageCode);
                
                logger.LogInformation("Generating catalog for {LanguageCode}", language.LanguageCode);
                await wikiGenerator.GenerateCatalogAsync(workspace, language, stoppingToken);
                
                logger.LogInformation("Generating documents for {LanguageCode}", language.LanguageCode);
                await wikiGenerator.GenerateDocumentsAsync(workspace, language, stoppingToken);
            }

            languageStopwatch.Stop();
            logger.LogInformation(
                "Language processing completed. LanguageId: {LanguageId}, Language: {LanguageCode}, Duration: {Duration}ms",
                language.Id, language.LanguageCode, languageStopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            languageStopwatch.Stop();
            logger.LogError(ex,
                "Language processing failed. LanguageId: {LanguageId}, Language: {LanguageCode}, Duration: {Duration}ms",
                language.Id, language.LanguageCode, languageStopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
