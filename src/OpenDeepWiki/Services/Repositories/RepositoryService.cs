using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models;
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.GitHub;

namespace OpenDeepWiki.Services.Repositories;

[MiniApi(Route = "/api/v1/repositories")]
[Tags("Repository")]
public class RepositoryService(IContext context, IGitPlatformService gitPlatformService, IUserContext userContext, IGitHubAppService gitHubAppService)
{
    [HttpPost("/submit")]
    public async Task<Repository> SubmitAsync([FromBody] RepositorySubmitRequest request)
    {
        var currentUserId = userContext.UserId;
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            throw new UnauthorizedAccessException("User not logged in");
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

        // Check if a repository with the same GitUrl + BranchName already exists
        var exists = await context.Repositories
            .AsNoTracking()
            .Where(r => r.GitUrl == request.GitUrl && !r.IsDeleted)
            .Join(context.RepositoryBranches, r => r.Id, b => b.RepositoryId, (r, b) => b)
            .AnyAsync(b => b.BranchName == request.BranchName);

        if (exists)
        {
            throw new InvalidOperationException("A repository with the same branch already exists, please do not submit duplicates");
        }

        // Get star and fork counts for public repositories
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
            IsPublic = effectiveIsPublic,
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
            IsDefault = true // The first language at submission time is the default language
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
            throw new InvalidOperationException("Repository not found");
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
    /// Get repository list (with status)
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

        // Sorting
        IOrderedQueryable<Repository> orderedQuery;
        var isDesc = string.IsNullOrWhiteSpace(sortOrder) || sortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase);
        
        if (sortBy?.Equals("updatedAt", StringComparison.OrdinalIgnoreCase) == true)
        {
            orderedQuery = isDesc ? query.OrderByDescending(r => r.UpdatedAt) : query.OrderBy(r => r.UpdatedAt);
        }
        else if (sortBy?.Equals("status", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Status sort priority: Processing(1) > Pending(0) > Completed(2) > Failed(3)
            // Using custom sort weights
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
    /// Update repository visibility
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
                    ErrorMessage = "User not logged in"
                }, statusCode: StatusCodes.Status401Unauthorized);
            }

            // Find repository
            var repository = await context.Repositories
                .FirstOrDefaultAsync(item => item.Id == request.RepositoryId);

            // Repository not found
            if (repository is null)
            {
                return Results.NotFound(new UpdateVisibilityResponse
                {
                    Id = request.RepositoryId,
                    IsPublic = request.IsPublic,
                    Success = false,
                    ErrorMessage = "Repository not found"
                });
            }

            // Verify ownership
            if (repository.OwnerUserId != currentUserId)
            {
                return Results.Json(new UpdateVisibilityResponse
                {
                    Id = request.RepositoryId,
                    IsPublic = repository.IsPublic,
                    Success = false,
                    ErrorMessage = "No permission to modify this repository"
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            // Update visibility
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
                ErrorMessage = "Internal server error"
            }, statusCode: StatusCodes.Status500InternalServerError);
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

    /// <summary>
    /// Regenerate repository documentation
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
                ErrorMessage = "User not logged in"
            };
        }

        var repository = await context.Repositories
            .FirstOrDefaultAsync(item => item.OrgName == request.Owner && item.RepoName == request.Repo);

        if (repository is null)
        {
            return new RegenerateResponse
            {
                Success = false,
                ErrorMessage = "Repository not found"
            };
        }

        // Verify ownership
        if (repository.OwnerUserId != currentUserId)
        {
            return new RegenerateResponse
            {
                Success = false,
                ErrorMessage = "No permission to operate on this repository"
            };
        }

        // Only failed or completed repositories can be regenerated
        if (repository.Status != RepositoryStatus.Failed && repository.Status != RepositoryStatus.Completed)
        {
            return new RegenerateResponse
            {
                Success = false,
                ErrorMessage = "Repository is being processed, cannot regenerate"
            };
        }

        // Get all branch language IDs for this repository
        var branchLanguageIds = await context.RepositoryBranches
            .Where(b => b.RepositoryId == repository.Id)
            .Join(context.BranchLanguages, b => b.Id, l => l.RepositoryBranchId, (b, l) => l.Id)
            .ToListAsync();

        // Clear previously generated document catalogs
        var oldCatalogs = await context.DocCatalogs
            .Where(c => branchLanguageIds.Contains(c.BranchLanguageId))
            .ToListAsync();

        // Collect associated document file IDs
        var docFileIds = oldCatalogs
            .Where(c => c.DocFileId != null)
            .Select(c => c.DocFileId!)
            .Distinct()
            .ToList();

        // Remove document catalogs
        if (oldCatalogs.Count > 0)
        {
            context.DocCatalogs.RemoveRange(oldCatalogs);
        }

        // Remove document files
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

        // Clear previous processing logs
        var oldLogs = await context.RepositoryProcessingLogs
            .Where(log => log.RepositoryId == repository.Id)
            .ToListAsync();
        
        if (oldLogs.Count > 0)
        {
            context.RepositoryProcessingLogs.RemoveRange(oldLogs);
        }

        // Reset status to Pending, the Worker will automatically pick it up for processing
        repository.Status = RepositoryStatus.Pending;
        await context.SaveChangesAsync();

        return new RegenerateResponse
        {
            Success = true
        };
    }

    /// <summary>
    /// Get repository branch list (from Git platform API)
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
