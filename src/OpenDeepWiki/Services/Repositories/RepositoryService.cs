using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models;
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.GitHub;
using OpenDeepWiki.Services.Organizations;

namespace OpenDeepWiki.Services.Repositories;

[MiniApi(Route = "/api/v1/repositories")]
[Tags("仓库")]
public class RepositoryService(
    IContext context,
    IGitPlatformService gitPlatformService,
    IUserContext userContext,
    IGitHubAppService gitHubAppService,
    IOrganizationService organizationService,
    IOptions<RepositoryAnalyzerOptions> repositoryOptions)
{
    [HttpPost("/submit")]
    public async Task<Repository> SubmitAsync([FromBody] RepositorySubmitRequest request)
    {
        var currentUserId = GetCurrentUserId();

        if (!request.IsPublic && string.IsNullOrWhiteSpace(request.AuthAccount) && string.IsNullOrWhiteSpace(request.AuthPassword))
        {
            throw new InvalidOperationException("仓库凭据为空时不允许设置为私有");
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

        // Server-side visibility verification: check actual GitHub repo visibility
        var effectiveIsPublic = request.IsPublic;
        if (IsPublicPlatform(request.GitUrl) && !string.IsNullOrWhiteSpace(request.OrgName) && !string.IsNullOrWhiteSpace(request.RepoName))
        {
            var repoInfo = await gitPlatformService.CheckRepoExistsAsync(request.OrgName, request.RepoName);
            if (repoInfo.Exists)
            {
                effectiveIsPublic = !repoInfo.IsPrivate;
            }
        }

        return await CreateRepositoryAsync(
            currentUserId,
            request.GitUrl,
            request.RepoName,
            request.OrgName,
            request.BranchName,
            request.LanguageCode,
            effectiveIsPublic,
            request.AuthAccount,
            request.AuthPassword,
            starCount,
            forkCount);
    }

    [HttpPost("/submit-archive")]
    public async Task<Repository> SubmitArchiveAsync([FromForm] ArchiveRepositorySubmitRequest request)
    {
        var currentUserId = GetCurrentUserId();

        if (request.Archive == null || request.Archive.Length <= 0)
        {
            throw new InvalidOperationException("请上传 ZIP 压缩包");
        }

        if (!request.Archive.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目前仅支持 ZIP 压缩包导入");
        }

        var branchName = NormalizeBranchName(request.BranchName);
        var uploadsDirectory = GetArchiveUploadsDirectory(currentUserId);
        Directory.CreateDirectory(uploadsDirectory);

        var uploadedArchivePath = Path.Combine(
            uploadsDirectory,
            $"{Guid.NewGuid():N}{Path.GetExtension(request.Archive.FileName)}");

        await using (var stream = new FileStream(uploadedArchivePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await request.Archive.CopyToAsync(stream);
        }

        return await CreateRepositoryAsync(
            currentUserId,
            RepositorySource.EncodeArchivePath(uploadedArchivePath),
            request.RepoName,
            request.OrgName,
            branchName,
            request.LanguageCode,
            request.IsPublic,
            null,
            null,
            0,
            0);
    }

    [HttpPost("/submit-local")]
    public async Task<Repository> SubmitLocalDirectoryAsync([FromBody] LocalDirectoryRepositorySubmitRequest request)
    {
        var currentUserId = GetCurrentUserId();
        var normalizedPath = NormalizeLocalDirectoryPath(request.LocalPath);

        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException("本地目录不存在");
        }

        EnsureLocalPathAllowed(normalizedPath);

        return await CreateRepositoryAsync(
            currentUserId,
            RepositorySource.EncodeLocalDirectoryPath(normalizedPath),
            request.RepoName,
            request.OrgName,
            NormalizeBranchName(request.BranchName),
            request.LanguageCode,
            request.IsPublic,
            null,
            null,
            0,
            0);
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
            query = query.Where(r => r.OwnerUserId == ownerId && !r.IsDepartmentOwned);
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
                GitUrl = r.SourceLocation,
                SourceType = r.SourceType,
                SourceLocation = r.SourceLocation,
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
                // Allow admin to manage department-owned repos in their departments
                var allowed = false;
                if (repository.IsDepartmentOwned)
                {
                    var isAdmin = userContext.User?.IsInRole("Admin") == true;
                    if (isAdmin)
                    {
                        var deptRepos = await organizationService.GetDepartmentRepositoriesAsync(currentUserId);
                        if (deptRepos.Any(r => r.RepositoryId == repository.Id))
                        {
                            allowed = true;
                        }
                    }
                }

                if (!allowed)
                {
                    return Results.Json(new UpdateVisibilityResponse
                    {
                        Id = request.RepositoryId,
                        IsPublic = repository.IsPublic,
                        Success = false,
                        ErrorMessage = "无权限修改此仓库"
                    }, statusCode: StatusCodes.Status403Forbidden);
                }
            }

            // 无密码仓库不能设为私有
            if (!request.IsPublic && string.IsNullOrWhiteSpace(repository.AuthPassword))
            {
                return Results.BadRequest(new UpdateVisibilityResponse
                {
                    Id = request.RepositoryId,
                    IsPublic = repository.IsPublic,
                    Success = false,
                    ErrorMessage = "仓库凭据为空时不允许设置为私有"
                });
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
        if (!RepositorySource.IsGit(gitUrl))
        {
            return false;
        }

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
            // Allow admin to manage department-owned repos in their departments
            var allowed = false;
            if (repository.IsDepartmentOwned)
            {
                var isAdmin = userContext.User?.IsInRole("Admin") == true;
                if (isAdmin)
                {
                    var deptRepos = await organizationService.GetDepartmentRepositoriesAsync(currentUserId);
                    if (deptRepos.Any(r => r.RepositoryId == repository.Id))
                    {
                        allowed = true;
                    }
                }
            }

            if (!allowed)
            {
                return new RegenerateResponse
                {
                    Success = false,
                    ErrorMessage = "无权限操作此仓库"
                };
            }
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

    private string GetCurrentUserId()
    {
        var currentUserId = userContext.UserId;
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            throw new UnauthorizedAccessException("用户未登录");
        }

        return currentUserId;
    }

    private async Task<Repository> CreateRepositoryAsync(
        string currentUserId,
        string storedSource,
        string repoName,
        string orgName,
        string branchName,
        string languageCode,
        bool isPublic,
        string? authAccount,
        string? authPassword,
        int starCount,
        int forkCount)
    {
        var exists = await context.Repositories
            .AsNoTracking()
            .Where(r => r.GitUrl == storedSource && !r.IsDeleted)
            .Join(context.RepositoryBranches, r => r.Id, b => b.RepositoryId, (r, b) => b)
            .AnyAsync(b => b.BranchName == branchName);

        if (exists)
        {
            throw new InvalidOperationException("该仓库的相同分支已存在，请勿重复提交");
        }

        var repositoryId = Guid.NewGuid().ToString();
        var repository = new Repository
        {
            Id = repositoryId,
            OwnerUserId = currentUserId,
            GitUrl = storedSource,
            RepoName = repoName,
            OrgName = orgName,
            AuthAccount = authAccount,
            AuthPassword = authPassword,
            IsPublic = isPublic,
            Status = RepositoryStatus.Pending,
            StarCount = starCount,
            ForkCount = forkCount
        };

        var branchId = Guid.NewGuid().ToString();
        var branch = new RepositoryBranch
        {
            Id = branchId,
            RepositoryId = repositoryId,
            BranchName = branchName
        };

        var language = new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = branchId,
            LanguageCode = languageCode,
            UpdateSummary = string.Empty,
            IsDefault = true
        };

        context.Repositories.Add(repository);
        context.RepositoryBranches.Add(branch);
        context.BranchLanguages.Add(language);

        await context.SaveChangesAsync();
        return repository;
    }

    private string GetArchiveUploadsDirectory(string currentUserId)
    {
        return Path.Combine(repositoryOptions.Value.RepositoriesDirectory, "_uploads", currentUserId);
    }

    private static string NormalizeBranchName(string? branchName)
    {
        return string.IsNullOrWhiteSpace(branchName) ? "main" : branchName.Trim();
    }

    private static string NormalizeLocalDirectoryPath(string path)
    {
        return Path.GetFullPath(path.Trim());
    }

    private void EnsureLocalPathAllowed(string fullPath)
    {
        var allowedRoots = repositoryOptions.Value.AllowedLocalPathRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.GetFullPath(root.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToList();

        if (allowedRoots.Count == 0)
        {
            throw new InvalidOperationException("当前未配置允许导入的本地目录根路径");
        }

        var normalizedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var isAllowed = allowedRoots.Any(root =>
            normalizedPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        if (!isAllowed)
        {
            throw new InvalidOperationException("本地目录不在允许导入的白名单范围内");
        }
    }
}
