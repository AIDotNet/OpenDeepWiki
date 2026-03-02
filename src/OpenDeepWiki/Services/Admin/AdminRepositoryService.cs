using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;

namespace OpenDeepWiki.Services.Admin;

/// <summary>
/// 管理端仓库服务实现
/// </summary>
public class AdminRepositoryService : IAdminRepositoryService
{
    private readonly IContext _context;
    private readonly IGitPlatformService _gitPlatformService;
    private readonly IRepositoryAnalyzer _repositoryAnalyzer;
    private readonly IWikiGenerator _wikiGenerator;

    public AdminRepositoryService(
        IContext context,
        IGitPlatformService gitPlatformService,
        IRepositoryAnalyzer repositoryAnalyzer,
        IWikiGenerator wikiGenerator)
    {
        _context = context;
        _gitPlatformService = gitPlatformService;
        _repositoryAnalyzer = repositoryAnalyzer;
        _wikiGenerator = wikiGenerator;
    }

    public async Task<AdminRepositoryListResponse> GetRepositoriesAsync(int page, int pageSize, string? search, int? status)
    {
        var query = _context.Repositories.Where(r => !r.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r => r.RepoName.Contains(search) || r.OrgName.Contains(search) || r.GitUrl.Contains(search));
        }

        if (status.HasValue)
        {
            query = query.Where(r => (int)r.Status == status.Value);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new AdminRepositoryDto
            {
                Id = r.Id,
                GitUrl = r.GitUrl,
                RepoName = r.RepoName,
                OrgName = r.OrgName,
                IsPublic = r.IsPublic,
                Status = (int)r.Status,
                StatusText = GetStatusText(r.Status),
                StarCount = r.StarCount,
                ForkCount = r.ForkCount,
                BookmarkCount = r.BookmarkCount,
                ViewCount = r.ViewCount,
                OwnerUserId = r.OwnerUserId,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            })
            .ToListAsync();

        return new AdminRepositoryListResponse
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<AdminRepositoryDto?> GetRepositoryByIdAsync(string id)
    {
        var repo = await _context.Repositories
            .Where(r => r.Id == id && !r.IsDeleted)
            .FirstOrDefaultAsync();

        if (repo == null) return null;

        return new AdminRepositoryDto
        {
            Id = repo.Id,
            GitUrl = repo.GitUrl,
            RepoName = repo.RepoName,
            OrgName = repo.OrgName,
            IsPublic = repo.IsPublic,
            Status = (int)repo.Status,
            StatusText = GetStatusText(repo.Status),
            StarCount = repo.StarCount,
            ForkCount = repo.ForkCount,
            BookmarkCount = repo.BookmarkCount,
            ViewCount = repo.ViewCount,
            OwnerUserId = repo.OwnerUserId,
            CreatedAt = repo.CreatedAt,
            UpdatedAt = repo.UpdatedAt
        };
    }

    public async Task<bool> UpdateRepositoryAsync(string id, UpdateRepositoryRequest request)
    {
        var repo = await _context.Repositories
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        if (repo == null) return false;

        if (request.IsPublic.HasValue)
            repo.IsPublic = request.IsPublic.Value;
        if (request.AuthAccount != null)
            repo.AuthAccount = request.AuthAccount;
        if (request.AuthPassword != null)
            repo.AuthPassword = request.AuthPassword;

        repo.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteRepositoryAsync(string id)
    {
        var repo = await _context.Repositories
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        if (repo == null) return false;

        repo.IsDeleted = true;
        repo.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateRepositoryStatusAsync(string id, int status)
    {
        var repo = await _context.Repositories
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        if (repo == null) return false;

        repo.Status = (RepositoryStatus)status;
        repo.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    private static string GetStatusText(RepositoryStatus status) => status switch
    {
        RepositoryStatus.Pending => "待处理",
        RepositoryStatus.Processing => "处理中",
        RepositoryStatus.Completed => "已完成",
        RepositoryStatus.Failed => "失败",
        _ => "未知"
    };

    public async Task<SyncStatsResult> SyncRepositoryStatsAsync(string id)
    {
        var repo = await _context.Repositories
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        if (repo == null)
        {
            return new SyncStatsResult { Success = false, Message = "仓库不存在" };
        }

        var stats = await _gitPlatformService.GetRepoStatsAsync(repo.GitUrl);
        if (stats == null)
        {
            return new SyncStatsResult { Success = false, Message = "无法获取仓库统计信息，可能是私有仓库或不支持的平台" };
        }

        repo.StarCount = stats.StarCount;
        repo.ForkCount = stats.ForkCount;
        repo.UpdatedAt = DateTime.UtcNow;

        // Sync visibility with actual Git platform state
        await SyncVisibilityAsync(repo);

        await _context.SaveChangesAsync();

        return new SyncStatsResult
        {
            Success = true,
            Message = "同步成功",
            StarCount = stats.StarCount,
            ForkCount = stats.ForkCount
        };
    }

    public async Task<BatchSyncStatsResult> BatchSyncRepositoryStatsAsync(string[] ids)
    {
        var result = new BatchSyncStatsResult
        {
            TotalCount = ids.Length
        };

        var repos = await _context.Repositories
            .Where(r => ids.Contains(r.Id) && !r.IsDeleted)
            .ToListAsync();

        foreach (var repo in repos)
        {
            var itemResult = new BatchSyncItemResult
            {
                Id = repo.Id,
                RepoName = $"{repo.OrgName}/{repo.RepoName}"
            };

            var stats = await _gitPlatformService.GetRepoStatsAsync(repo.GitUrl);
            if (stats != null)
            {
                repo.StarCount = stats.StarCount;
                repo.ForkCount = stats.ForkCount;
                repo.UpdatedAt = DateTime.UtcNow;

                // Sync visibility with actual Git platform state
                await SyncVisibilityAsync(repo);

                itemResult.Success = true;
                itemResult.StarCount = stats.StarCount;
                itemResult.ForkCount = stats.ForkCount;
                result.SuccessCount++;
            }
            else
            {
                itemResult.Success = false;
                itemResult.Message = "无法获取统计信息";
                result.FailedCount++;
            }

            result.Results.Add(itemResult);
        }

        // 处理不存在的仓库
        var foundIds = repos.Select(r => r.Id).ToHashSet();
        foreach (var id in ids.Where(id => !foundIds.Contains(id)))
        {
            result.Results.Add(new BatchSyncItemResult
            {
                Id = id,
                Success = false,
                Message = "仓库不存在"
            });
            result.FailedCount++;
        }

        await _context.SaveChangesAsync();
        return result;
    }

    public async Task<BatchDeleteResult> BatchDeleteRepositoriesAsync(string[] ids)
    {
        var result = new BatchDeleteResult
        {
            TotalCount = ids.Length
        };

        var repos = await _context.Repositories
            .Where(r => ids.Contains(r.Id) && !r.IsDeleted)
            .ToListAsync();

        foreach (var repo in repos)
        {
            repo.IsDeleted = true;
            repo.UpdatedAt = DateTime.UtcNow;
            result.SuccessCount++;
        }

        // 记录不存在的仓库
        var foundIds = repos.Select(r => r.Id).ToHashSet();
        result.FailedIds = ids.Where(id => !foundIds.Contains(id)).ToList();
        result.FailedCount = result.FailedIds.Count;

        await _context.SaveChangesAsync();
        return result;
    }

    public async Task<AdminRepositoryManagementDto?> GetRepositoryManagementAsync(string id)
    {
        var repository = await _context.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        if (repository == null)
        {
            return null;
        }

        var branches = await _context.RepositoryBranches
            .AsNoTracking()
            .Where(b => b.RepositoryId == id && !b.IsDeleted)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();

        var branchIds = branches.Select(b => b.Id).ToList();
        var branchNameMap = branches.ToDictionary(b => b.Id, b => b.BranchName);

        var languages = await _context.BranchLanguages
            .AsNoTracking()
            .Where(l => branchIds.Contains(l.RepositoryBranchId) && !l.IsDeleted)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        var languageIds = languages.Select(l => l.Id).ToList();

        var catalogStats = await _context.DocCatalogs
            .AsNoTracking()
            .Where(c => languageIds.Contains(c.BranchLanguageId) && !c.IsDeleted)
            .GroupBy(c => c.BranchLanguageId)
            .Select(g => new
            {
                BranchLanguageId = g.Key,
                CatalogCount = g.Count(),
                DocumentCount = g.Count(c => c.DocFileId != null)
            })
            .ToListAsync();

        var statsMap = catalogStats.ToDictionary(
            item => item.BranchLanguageId,
            item => (item.CatalogCount, item.DocumentCount));

        var languageGroups = languages
            .GroupBy(l => l.RepositoryBranchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var branchDtos = branches.Select(branch =>
        {
            languageGroups.TryGetValue(branch.Id, out var branchLanguages);
            var languageDtos = (branchLanguages ?? new List<BranchLanguage>())
                .Select(language =>
                {
                    var stats = statsMap.TryGetValue(language.Id, out var value) ? value : (0, 0);
                    return new AdminBranchLanguageDto
                    {
                        Id = language.Id,
                        LanguageCode = language.LanguageCode,
                        IsDefault = language.IsDefault,
                        CatalogCount = stats.Item1,
                        DocumentCount = stats.Item2,
                        CreatedAt = language.CreatedAt
                    };
                })
                .OrderByDescending(language => language.IsDefault)
                .ThenBy(language => language.LanguageCode)
                .ToList();

            return new AdminRepositoryBranchDto
            {
                Id = branch.Id,
                Name = branch.BranchName,
                LastCommitId = branch.LastCommitId,
                LastProcessedAt = branch.LastProcessedAt,
                Languages = languageDtos
            };
        }).ToList();

        var recentTasks = await _context.IncrementalUpdateTasks
            .AsNoTracking()
            .Where(t => t.RepositoryId == id && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .Take(20)
            .ToListAsync();

        var taskDtos = recentTasks.Select(task =>
            new AdminIncrementalTaskDto
            {
                TaskId = task.Id,
                BranchId = task.BranchId,
                BranchName = branchNameMap.GetValueOrDefault(task.BranchId),
                Status = task.Status.ToString(),
                Priority = task.Priority,
                IsManualTrigger = task.IsManualTrigger,
                RetryCount = task.RetryCount,
                PreviousCommitId = task.PreviousCommitId,
                TargetCommitId = task.TargetCommitId,
                ErrorMessage = task.ErrorMessage,
                CreatedAt = task.CreatedAt,
                StartedAt = task.StartedAt,
                CompletedAt = task.CompletedAt
            }).ToList();

        return new AdminRepositoryManagementDto
        {
            RepositoryId = repository.Id,
            OrgName = repository.OrgName,
            RepoName = repository.RepoName,
            Status = (int)repository.Status,
            StatusText = GetStatusText(repository.Status),
            Branches = branchDtos,
            RecentIncrementalTasks = taskDtos
        };
    }

    public async Task<AdminRepositoryOperationResult> RegenerateRepositoryAsync(string id)
    {
        var repository = await _context.Repositories
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        if (repository == null)
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "仓库不存在"
            };
        }

        if (repository.Status == RepositoryStatus.Pending || repository.Status == RepositoryStatus.Processing)
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "仓库正在处理中，无法重复触发"
            };
        }

        var branchLanguageIds = await _context.RepositoryBranches
            .Where(b => b.RepositoryId == repository.Id && !b.IsDeleted)
            .Join(
                _context.BranchLanguages.Where(l => !l.IsDeleted),
                b => b.Id,
                l => l.RepositoryBranchId,
                (b, l) => l.Id)
            .ToListAsync();

        var oldCatalogs = await _context.DocCatalogs
            .Where(c => branchLanguageIds.Contains(c.BranchLanguageId) && !c.IsDeleted)
            .ToListAsync();

        var docFileIds = oldCatalogs
            .Where(c => c.DocFileId != null)
            .Select(c => c.DocFileId!)
            .Distinct()
            .ToList();

        if (oldCatalogs.Count > 0)
        {
            _context.DocCatalogs.RemoveRange(oldCatalogs);
        }

        if (docFileIds.Count > 0)
        {
            var oldDocFiles = await _context.DocFiles
                .Where(file => docFileIds.Contains(file.Id))
                .ToListAsync();
            if (oldDocFiles.Count > 0)
            {
                _context.DocFiles.RemoveRange(oldDocFiles);
            }
        }

        var oldLogs = await _context.RepositoryProcessingLogs
            .Where(log => log.RepositoryId == repository.Id)
            .ToListAsync();
        if (oldLogs.Count > 0)
        {
            _context.RepositoryProcessingLogs.RemoveRange(oldLogs);
        }

        repository.Status = RepositoryStatus.Pending;
        repository.UpdateTimestamp();
        await _context.SaveChangesAsync();

        return new AdminRepositoryOperationResult
        {
            Success = true,
            Message = "已触发全量重生成"
        };
    }

    public async Task<AdminRepositoryOperationResult> RegenerateDocumentAsync(
        string id,
        RegenerateRepositoryDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BranchId) ||
            string.IsNullOrWhiteSpace(request.LanguageCode) ||
            string.IsNullOrWhiteSpace(request.DocumentPath))
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "请求参数不完整"
            };
        }

        var repository = await _context.Repositories
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken);
        if (repository == null)
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "仓库不存在"
            };
        }

        var branch = await _context.RepositoryBranches
            .FirstOrDefaultAsync(
                b => b.Id == request.BranchId && b.RepositoryId == id && !b.IsDeleted,
                cancellationToken);
        if (branch == null)
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "分支不存在"
            };
        }

        var normalizedLanguage = request.LanguageCode.Trim();
        var branchLanguage = await _context.BranchLanguages
            .FirstOrDefaultAsync(
                l => l.RepositoryBranchId == branch.Id &&
                     !l.IsDeleted &&
                     l.LanguageCode.ToLower() == normalizedLanguage.ToLower(),
                cancellationToken);
        if (branchLanguage == null)
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "语言不存在"
            };
        }

        var normalizedPath = NormalizeDocPath(request.DocumentPath);
        var catalogExists = await _context.DocCatalogs.AnyAsync(
            c => c.BranchLanguageId == branchLanguage.Id &&
                 c.Path == normalizedPath &&
                 !c.IsDeleted,
            cancellationToken);
        if (!catalogExists)
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "文档不存在"
            };
        }

        if (_wikiGenerator is WikiGenerator generator)
        {
            generator.SetCurrentRepository(repository.Id);
        }

        try
        {
            var workspace = await _repositoryAnalyzer.PrepareWorkspaceAsync(
                repository,
                branch.BranchName,
                branch.LastCommitId,
                cancellationToken);

            try
            {
                await _wikiGenerator.RegenerateDocumentAsync(
                    workspace,
                    branchLanguage,
                    normalizedPath,
                    cancellationToken);
            }
            finally
            {
                await _repositoryAnalyzer.CleanupWorkspaceAsync(workspace, cancellationToken);
            }

            repository.UpdateTimestamp();
            await _context.SaveChangesAsync(cancellationToken);

            return new AdminRepositoryOperationResult
            {
                Success = true,
                Message = "文档重生成已完成"
            };
        }
        catch (Exception ex)
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = $"文档重生成失败: {ex.Message}"
            };
        }
    }

    public async Task<AdminRepositoryOperationResult> UpdateDocumentContentAsync(
        string id,
        UpdateRepositoryDocumentContentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BranchId) ||
            string.IsNullOrWhiteSpace(request.LanguageCode) ||
            string.IsNullOrWhiteSpace(request.DocumentPath))
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "请求参数不完整"
            };
        }

        var repository = await _context.Repositories
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, cancellationToken);
        if (repository == null)
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "仓库不存在"
            };
        }

        var branch = await _context.RepositoryBranches
            .FirstOrDefaultAsync(
                b => b.Id == request.BranchId && b.RepositoryId == id && !b.IsDeleted,
                cancellationToken);
        if (branch == null)
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "分支不存在"
            };
        }

        var normalizedLanguage = request.LanguageCode.Trim();
        var branchLanguage = await _context.BranchLanguages
            .FirstOrDefaultAsync(
                l => l.RepositoryBranchId == branch.Id &&
                     !l.IsDeleted &&
                     l.LanguageCode.ToLower() == normalizedLanguage.ToLower(),
                cancellationToken);
        if (branchLanguage == null)
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "语言不存在"
            };
        }

        var normalizedPath = NormalizeDocPath(request.DocumentPath);
        var catalog = await _context.DocCatalogs
            .FirstOrDefaultAsync(
                c => c.BranchLanguageId == branchLanguage.Id &&
                     c.Path == normalizedPath &&
                     !c.IsDeleted,
                cancellationToken);

        if (catalog == null || string.IsNullOrWhiteSpace(catalog.DocFileId))
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "文档不存在或不可编辑"
            };
        }

        var docFile = await _context.DocFiles
            .FirstOrDefaultAsync(f => f.Id == catalog.DocFileId && !f.IsDeleted, cancellationToken);
        if (docFile == null)
        {
            return new AdminRepositoryOperationResult
            {
                Success = false,
                Message = "文档文件不存在"
            };
        }

        docFile.Content = request.Content ?? string.Empty;
        docFile.UpdateTimestamp();
        repository.UpdateTimestamp();

        _context.RepositoryProcessingLogs.Add(new RepositoryProcessingLog
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            Step = ProcessingStep.Content,
            Message = $"管理端手动更新文档：{normalizedPath}",
            IsAiOutput = false,
            ToolName = "AdminDocEditor",
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new AdminRepositoryOperationResult
        {
            Success = true,
            Message = "文档内容已保存"
        };
    }

    /// <summary>
    /// Sync repository visibility with the actual Git platform state
    /// </summary>
    private async Task SyncVisibilityAsync(Repository repo)
    {
        try
        {
            if (!IsPublicPlatform(repo.GitUrl) ||
                string.IsNullOrWhiteSpace(repo.OrgName) ||
                string.IsNullOrWhiteSpace(repo.RepoName))
            {
                return;
            }

            var repoInfo = await _gitPlatformService.CheckRepoExistsAsync(repo.OrgName, repo.RepoName);
            if (!repoInfo.Exists)
            {
                return;
            }

            var shouldBePublic = !repoInfo.IsPrivate;
            if (repo.IsPublic != shouldBePublic)
            {
                repo.IsPublic = shouldBePublic;
            }
        }
        catch
        {
            // Visibility sync is best-effort; don't fail the parent operation
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

    private static string NormalizeDocPath(string path)
    {
        return path.Trim().Trim('/');
    }
}
