using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models;
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.GitHub;

namespace OpenDeepWiki.Services.Repositories;

[MiniApi(Route = "/api/v1/repositories")]
[Tags("仓库")]
public class RepositoryService(IContext context, IGitPlatformService gitPlatformService, IUserContext userContext, IGitHubAppService gitHubAppService)
{
    [HttpPost("/submit")]
    public async Task<Repository> SubmitAsync([FromBody] RepositorySubmitRequest request)
    {
        var currentUserId = userContext.UserId;
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            throw new UnauthorizedAccessException("用户未登录");
        }

        if (!request.IsPublic && string.IsNullOrWhiteSpace(request.AuthAccount) && string.IsNullOrWhiteSpace(request.AuthPassword))
        {
            // Allow private repos without credentials if a GitHub App installation exists for the org
            var hasAppInstallation = gitHubAppService.IsConfigured &&
                !string.IsNullOrWhiteSpace(request.OrgName) &&
                await context.GitHubAppInstallations.AnyAsync(
                    i => i.AccountLogin == request.OrgName && !i.IsDeleted);

            if (!hasAppInstallation)
            {
                throw new InvalidOperationException("Private repositories require credentials or a GitHub App installation for the organization");
            }
        }

        // 校验是否已存在相同仓库（相同 GitUrl + BranchName）
        var exists = await context.Repositories
            .AsNoTracking()
            .Where(r => r.GitUrl == request.GitUrl && !r.IsDeleted)
            .Join(context.RepositoryBranches, r => r.Id, b => b.RepositoryId, (r, b) => b)
            .AnyAsync(b => b.BranchName == request.BranchName);

        if (exists)
        {
            throw new InvalidOperationException("该仓库的相同分支已存在，请勿重复提交");
        }

        // 获取公开仓库的star和fork数
        int starCount = 0;
        int forkCount = 0;
        
        if (string.IsNullOrWhiteSpace(request.AuthPassword) && IsPublicPlatform(request.GitUrl))
        {
            var stats = await gitPlatformService.GetRepoStatsAsync(request.GitUrl);
            if (stats != null)
            {
                starCount = stats.StarCount;
                forkCount = stats.ForkCount;
            }
        }

        var repositoryId = Guid.NewGuid().ToString();
        var repository = new Repository
        {
            Id = repositoryId,
            OwnerUserId = currentUserId,
            GitUrl = request.GitUrl,
            RepoName = request.RepoName,
            OrgName = request.OrgName,
            AuthAccount = request.AuthAccount,
            AuthPassword = request.AuthPassword,
            IsPublic = request.IsPublic,
            Status = RepositoryStatus.Pending,
            StarCount = starCount,
            ForkCount = forkCount
        };

        var branchId = Guid.NewGuid().ToString();
        var branch = new RepositoryBranch
        {
            Id = branchId,
            RepositoryId = repositoryId,
            BranchName = request.BranchName
        };

        var language = new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = branchId,
            LanguageCode = request.LanguageCode,
            UpdateSummary = string.Empty,
            IsDefault = true // 提交时的第一个语言为默认语言
        };

        context.Repositories.Add(repository);
        context.RepositoryBranches.Add(branch);
        context.BranchLanguages.Add(language);

        await context.SaveChangesAsync();
        return repository;
    }

    [HttpPost("/assign")]
    public async Task<RepositoryAssignment> AssignAsync([FromBody] RepositoryAssignRequest request)
    {
        var repository = await context.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == request.RepositoryId);

        if (repository is null)
        {
            throw new InvalidOperationException("仓库不存在");
        }

        var assignment = new RepositoryAssignment
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = request.RepositoryId,
            DepartmentId = request.DepartmentId,
            AssigneeUserId = request.AssigneeUserId
        };

        context.RepositoryAssignments.Add(assignment);
        await context.SaveChangesAsync();
        return assignment;
    }

    /// <summary>
    /// 获取仓库列表（含状态）
    /// </summary>
    [HttpGet("/list")]
    public async Task<RepositoryListResponse> GetListAsync(
        [FromQuery] bool? isPublic = null,
        [FromQuery] string? keyword = null,
        [FromQuery] string? language = null,
        [FromQuery] string? ownerId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        [FromQuery] RepositoryStatus? status = null)
    {
        var query = context.Repositories.AsNoTracking().Where(r => !r.IsDeleted);

        if (!string.IsNullOrWhiteSpace(ownerId))
        {
            query = query.Where(r => r.OwnerUserId == ownerId);
        }

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        if (isPublic.HasValue)
        {
            query = query.Where(r => r.IsPublic == isPublic.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var lowerKeyword = keyword.ToLower();
            query = query.Where(r => 
                r.OrgName.ToLower().Contains(lowerKeyword) || 
                r.RepoName.ToLower().Contains(lowerKeyword));
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            query = query.Where(r => r.PrimaryLanguage == language);
        }

        var total = await query.CountAsync();

        // 排序
        IOrderedQueryable<Repository> orderedQuery;
        var isDesc = string.IsNullOrWhiteSpace(sortOrder) || sortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase);
        
        if (sortBy?.Equals("updatedAt", StringComparison.OrdinalIgnoreCase) == true)
        {
            orderedQuery = isDesc ? query.OrderByDescending(r => r.UpdatedAt) : query.OrderBy(r => r.UpdatedAt);
        }
        else if (sortBy?.Equals("status", StringComparison.OrdinalIgnoreCase) == true)
        {
            // 状态排序优先级: Processing(1) > Pending(0) > Completed(2) > Failed(3)
            // 使用自定义排序权重
            orderedQuery = query.OrderBy(r => 
                r.Status == RepositoryStatus.Processing ? 0 :
                r.Status == RepositoryStatus.Pending ? 1 :
                r.Status == RepositoryStatus.Completed ? 2 : 3)
                .ThenByDescending(r => r.CreatedAt);
        }
        else
        {
            orderedQuery = isDesc ? query.OrderByDescending(r => r.CreatedAt) : query.OrderBy(r => r.CreatedAt);
        }

        var repositories = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new RepositoryListResponse
        {
            Total = total,
            Items = repositories.Select(r => new RepositoryItemResponse
            {
                Id = r.Id,
                OrgName = r.OrgName,
                RepoName = r.RepoName,
                GitUrl = r.GitUrl,
                Status = r.Status,
                IsPublic = r.IsPublic,
                HasPassword = !string.IsNullOrWhiteSpace(r.AuthPassword),
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                StarCount = r.StarCount,
                ForkCount = r.ForkCount,
                PrimaryLanguage = r.PrimaryLanguage
            }).ToList()
        };
    }

    /// <summary>
    /// 更新仓库可见性
    /// </summary>
    [HttpPost("/visibility")]
    public async Task<IResult> UpdateVisibilityAsync([FromBody] UpdateVisibilityRequest request)
    {
        try
        {
            var currentUserId = userContext.UserId;
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                return Results.Json(new UpdateVisibilityResponse
                {
                    Id = request.RepositoryId,
                    IsPublic = request.IsPublic,
                    Success = false,
                    ErrorMessage = "用户未登录"
                }, statusCode: StatusCodes.Status401Unauthorized);
            }

            // 查找仓库
            var repository = await context.Repositories
                .FirstOrDefaultAsync(item => item.Id == request.RepositoryId);

            // 仓库不存在
            if (repository is null)
            {
                return Results.NotFound(new UpdateVisibilityResponse
                {
                    Id = request.RepositoryId,
                    IsPublic = request.IsPublic,
                    Success = false,
                    ErrorMessage = "仓库不存在"
                });
            }

            // 验证所有权
            if (repository.OwnerUserId != currentUserId)
            {
                return Results.Json(new UpdateVisibilityResponse
                {
                    Id = request.RepositoryId,
                    IsPublic = repository.IsPublic,
                    Success = false,
                    ErrorMessage = "无权限修改此仓库"
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            // Allow private repos without credentials if a GitHub App installation exists for the org
            if (!request.IsPublic && string.IsNullOrWhiteSpace(repository.AuthPassword))
            {
                var hasAppInstallation = gitHubAppService.IsConfigured &&
                    !string.IsNullOrWhiteSpace(repository.OrgName) &&
                    await context.GitHubAppInstallations.AnyAsync(
                        i => i.AccountLogin == repository.OrgName && !i.IsDeleted);

                if (!hasAppInstallation)
                {
                    return Results.BadRequest(new UpdateVisibilityResponse
                    {
                        Id = request.RepositoryId,
                        IsPublic = repository.IsPublic,
                        Success = false,
                        ErrorMessage = "Private repositories require credentials or a GitHub App installation for the organization"
                    });
                }
            }

            // 更新可见性
            repository.IsPublic = request.IsPublic;
            await context.SaveChangesAsync();

            return Results.Ok(new UpdateVisibilityResponse
            {
                Id = repository.Id,
                IsPublic = repository.IsPublic,
                Success = true,
                ErrorMessage = null
            });
        }
        catch (Exception)
        {
            return Results.Json(new UpdateVisibilityResponse
            {
                Id = request.RepositoryId,
                IsPublic = request.IsPublic,
                Success = false,
                ErrorMessage = "服务器内部错误"
            }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// 判断是否为支持获取统计信息的公开平台
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
    /// 重新生成仓库文档
    /// </summary>
    [HttpPost("/regenerate")]
    public async Task<RegenerateResponse> RegenerateAsync([FromBody] RegenerateRequest request)
    {
        var currentUserId = userContext.UserId;
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return new RegenerateResponse
            {
                Success = false,
                ErrorMessage = "用户未登录"
            };
        }

        var repository = await context.Repositories
            .FirstOrDefaultAsync(item => item.OrgName == request.Owner && item.RepoName == request.Repo);

        if (repository is null)
        {
            return new RegenerateResponse
            {
                Success = false,
                ErrorMessage = "仓库不存在"
            };
        }

        // 验证所有权
        if (repository.OwnerUserId != currentUserId)
        {
            return new RegenerateResponse
            {
                Success = false,
                ErrorMessage = "无权限操作此仓库"
            };
        }

        // 只有失败或完成状态才能重新生成
        if (repository.Status != RepositoryStatus.Failed && repository.Status != RepositoryStatus.Completed)
        {
            return new RegenerateResponse
            {
                Success = false,
                ErrorMessage = "仓库正在处理中，无法重新生成"
            };
        }

        // 获取该仓库的所有分支语言ID
        var branchLanguageIds = await context.RepositoryBranches
            .Where(b => b.RepositoryId == repository.Id)
            .Join(context.BranchLanguages, b => b.Id, l => l.RepositoryBranchId, (b, l) => l.Id)
            .ToListAsync();

        // 清空之前生成的文档目录
        var oldCatalogs = await context.DocCatalogs
            .Where(c => branchLanguageIds.Contains(c.BranchLanguageId))
            .ToListAsync();

        // 收集关联的文档文件ID
        var docFileIds = oldCatalogs
            .Where(c => c.DocFileId != null)
            .Select(c => c.DocFileId!)
            .Distinct()
            .ToList();

        // 清空文档目录
        if (oldCatalogs.Count > 0)
        {
            context.DocCatalogs.RemoveRange(oldCatalogs);
        }

        // 清空文档文件
        if (docFileIds.Count > 0)
        {
            var oldDocFiles = await context.DocFiles
                .Where(f => docFileIds.Contains(f.Id))
                .ToListAsync();
            
            if (oldDocFiles.Count > 0)
            {
                context.DocFiles.RemoveRange(oldDocFiles);
            }
        }

        // 清空之前的处理日志
        var oldLogs = await context.RepositoryProcessingLogs
            .Where(log => log.RepositoryId == repository.Id)
            .ToListAsync();
        
        if (oldLogs.Count > 0)
        {
            context.RepositoryProcessingLogs.RemoveRange(oldLogs);
        }

        // 重置状态为 Pending，Worker 会自动拾取处理
        repository.Status = RepositoryStatus.Pending;
        await context.SaveChangesAsync();

        return new RegenerateResponse
        {
            Success = true
        };
    }

    /// <summary>
    /// 获取仓库分支列表（从Git平台API获取）
    /// </summary>
    [HttpGet("/branches")]
    public async Task<GitBranchesResponse> GetBranchesAsync([FromQuery] string gitUrl)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
        {
            return new GitBranchesResponse
            {
                Branches = [],
                DefaultBranch = null,
                IsSupported = false
            };
        }

        var result = await gitPlatformService.GetBranchesAsync(gitUrl);
        
        return new GitBranchesResponse
        {
            Branches = result.Branches.Select(b => new GitBranchItem
            {
                Name = b.Name,
                IsDefault = b.IsDefault
            }).ToList(),
            DefaultBranch = result.DefaultBranch,
            IsSupported = result.IsSupported
        };
    }
}
