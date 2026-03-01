using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.Cache.Abstractions;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models;
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.Organizations;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Authorization;

namespace OpenDeepWiki.Services.Repositories;

[MiniApi(Route = "/api/v1/repos")]
[Tags("Repository Documents")]
public class RepositoryDocsService(IContext context, IGitPlatformService gitPlatformService, ICache cache, IUserContext userContext, IOrganizationService organizationService)
{
    private const string FallbackLanguageCode = "zh"; // Fallback language when no default language is marked
    private const int ExportRateLimitMinutes = 5; // Export rate limit: only one export allowed per 5 minutes
    private const int MaxConcurrentExports = 10; // Maximum concurrent exports
    private const string ExportRateLimitKeyPrefix = "export:rate-limit";
    private const string ExportConcurrencyCountKey = "export:concurrency:count";
    private const string ExportConcurrencyLockKey = "export:concurrency:lock";
    private static readonly TimeSpan ExportConcurrencyLockTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExportConcurrencyCountTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ExportRateLimitTtl = TimeSpan.FromMinutes(ExportRateLimitMinutes);

    [HttpGet("/{owner}/{repo}/branches")]
    public async Task<RepositoryBranchesResponse> GetBranchesAsync(string owner, string repo)
    {
        var repository = await context.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.OrgName == owner && item.RepoName == repo);

        if (repository is null)
        {
            return new RepositoryBranchesResponse { Branches = [], Languages = [] };
        }

        if (!await CanAccessRepositoryAsync(repository))
        {
            return new RepositoryBranchesResponse { Branches = [], Languages = [] };
        }

        var branches = await context.RepositoryBranches
            .AsNoTracking()
            .Where(item => item.RepositoryId == repository.Id)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();

        var branchItems = new List<BranchItem>();
        var allLanguages = new HashSet<string>();
        string? defaultLanguageCode = null;

        foreach (var branch in branches)
        {
            // Get languages with actual document content under this branch
            var languagesWithContent = await context.BranchLanguages
                .AsNoTracking()
                .Where(item => item.RepositoryBranchId == branch.Id)
                .Where(item => context.DocCatalogs.Any(c => c.BranchLanguageId == item.Id && !c.IsDeleted))
                .ToListAsync();

            // Only add if the branch has content
            if (languagesWithContent.Count > 0)
            {
                branchItems.Add(new BranchItem
                {
                    Name = branch.BranchName,
                    Languages = languagesWithContent.Select(l => l.LanguageCode).ToList()
                });

                foreach (var lang in languagesWithContent)
                {
                    allLanguages.Add(lang.LanguageCode);
                    // Record the language marked as default
                    if (lang.IsDefault && defaultLanguageCode is null)
                    {
                        defaultLanguageCode = lang.LanguageCode;
                    }
                }
            }
        }

        // Determine default branch (only select from branches with content)
        var defaultBranch = branchItems.FirstOrDefault(b => 
            string.Equals(b.Name, "main", StringComparison.OrdinalIgnoreCase))?.Name
            ?? branchItems.FirstOrDefault(b => 
                string.Equals(b.Name, "master", StringComparison.OrdinalIgnoreCase))?.Name
            ?? branchItems.FirstOrDefault()?.Name
            ?? "";

        // Determine default language: prefer the marked default language, otherwise fallback
        var finalDefaultLanguage = defaultLanguageCode 
            ?? (allLanguages.Contains(FallbackLanguageCode) ? FallbackLanguageCode : allLanguages.FirstOrDefault() ?? "");

        return new RepositoryBranchesResponse
        {
            Branches = branchItems,
            Languages = allLanguages.ToList(),
            DefaultBranch = defaultBranch,
            DefaultLanguage = finalDefaultLanguage
        };
    }

    [HttpGet("/{owner}/{repo}/tree")]
    public async Task<RepositoryTreeResponse> GetTreeAsync(string owner, string repo, [FromQuery] string? branch = null, [FromQuery] string? lang = null)
    {
        var repository = await context.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.OrgName == owner && item.RepoName == repo);

        // Repository does not exist
        if (repository is null)
        {
            return new RepositoryTreeResponse
            {
                Owner = owner,
                Repo = repo,
                Exists = false,
                Status = RepositoryStatus.Pending,
                Nodes = []
            };
        }

        // Access control: restricted repos require authentication + ownership/department
        if (!await CanAccessRepositoryAsync(repository))
        {
            return new RepositoryTreeResponse
            {
                Owner = owner,
                Repo = repo,
                Exists = false,
                Status = RepositoryStatus.Pending,
                Nodes = []
            };
        }

        // Repository is being processed or awaiting processing
        if (repository.Status == RepositoryStatus.Pending || repository.Status == RepositoryStatus.Processing)
        {
            return new RepositoryTreeResponse
            {
                Owner = repository.OrgName,
                Repo = repository.RepoName,
                Exists = true,
                Status = repository.Status,
                Nodes = []
            };
        }

        // Repository processing failed
        if (repository.Status == RepositoryStatus.Failed)
        {
            return new RepositoryTreeResponse
            {
                Owner = repository.OrgName,
                Repo = repository.RepoName,
                Exists = true,
                Status = repository.Status,
                Nodes = []
            };
        }

        // Repository processing completed, get document catalog
        var branchEntity = await GetBranchAsync(repository.Id, branch);
        var language = await GetLanguageAsync(branchEntity.Id, lang);

        var catalogs = await context.DocCatalogs
            .AsNoTracking()
            .Where(item => item.BranchLanguageId == language.Id && !item.IsDeleted)
            .OrderBy(item => item.Order)
            .ToListAsync();

        if (catalogs.Count == 0)
        {
            // Repository completed but has no documents, may be an empty repository
            return new RepositoryTreeResponse
            {
                Owner = repository.OrgName,
                Repo = repository.RepoName,
                Exists = true,
                Status = repository.Status,
                Nodes = []
            };
        }

        // Build tree structure
        var catalogMap = catalogs.ToDictionary(c => c.Id);
        var rootNodes = new List<RepositoryTreeNodeResponse>();

        foreach (var catalog in catalogs.Where(c => c.ParentId == null))
        {
            rootNodes.Add(BuildTreeNode(catalog, catalogMap));
        }

        // Recursively find the first document with actual content
        var defaultSlug = FindFirstContentSlug(catalogs, null) ?? string.Empty;

        return new RepositoryTreeResponse
        {
            Owner = repository.OrgName,
            Repo = repository.RepoName,
            DefaultSlug = defaultSlug,
            Nodes = rootNodes,
            Exists = true,
            Status = repository.Status,
            CurrentBranch = branchEntity.BranchName,
            CurrentLanguage = language.LanguageCode
        };
    }

    [HttpGet("/{owner}/{repo}/docs/{*slug}")]
    public async Task<RepositoryDocResponse> GetDocAsync(string owner, string repo, string slug, [FromQuery] string? branch = null, [FromQuery] string? lang = null)
    {
        var normalizedSlug = NormalizePath(slug);

        var repository = await GetRepositoryAsync(owner, repo);
        if (repository is null)
        {
            return new RepositoryDocResponse { Slug = normalizedSlug, Exists = false };
        }

        // Access control: restricted repos require authentication + ownership/department
        if (!await CanAccessRepositoryAsync(repository))
        {
            return new RepositoryDocResponse { Slug = normalizedSlug, Exists = false };
        }

        var branchEntity = await GetBranchAsync(repository.Id, branch);
        if (branchEntity is null)
        {
            return new RepositoryDocResponse { Slug = normalizedSlug, Exists = false };
        }

        var language = await GetLanguageAsync(branchEntity.Id, lang);
        if (language is null)
        {
            return new RepositoryDocResponse { Slug = normalizedSlug, Exists = false };
        }

        var catalog = await context.DocCatalogs
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.BranchLanguageId == language.Id && item.Path == normalizedSlug && !item.IsDeleted);

        if (catalog is null || catalog.DocFileId is null)
        {
            return new RepositoryDocResponse { Slug = normalizedSlug, Exists = false };
        }

        var docFile = await context.DocFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == catalog.DocFileId);

        if (docFile is null)
        {
            return new RepositoryDocResponse { Slug = normalizedSlug, Exists = false };
        }

        // Parse source file list
        var sourceFiles = new List<string>();
        if (!string.IsNullOrEmpty(docFile.SourceFiles))
        {
            try
            {
                sourceFiles = System.Text.Json.JsonSerializer.Deserialize<List<string>>(docFile.SourceFiles) ?? [];
            }
            catch
            {
                // Return empty list when parsing fails
            }
        }

        return new RepositoryDocResponse
        {
            Slug = normalizedSlug,
            Content = docFile.Content,
            SourceFiles = sourceFiles,
            Exists = true
        };
    }

    /// <summary>
    /// Check if a GitHub repository exists
    /// </summary>
    [HttpGet("/{owner}/{repo}/check")]
    public async Task<GitRepoCheckResponse> CheckRepoAsync(string owner, string repo)
    {
        var repoInfo = await gitPlatformService.CheckRepoExistsAsync(owner, repo);
        
        return new GitRepoCheckResponse
        {
            Exists = repoInfo.Exists,
            Name = repoInfo.Name,
            Description = repoInfo.Description,
            DefaultBranch = repoInfo.DefaultBranch,
            StarCount = repoInfo.StarCount,
            ForkCount = repoInfo.ForkCount,
            Language = repoInfo.Language,
            AvatarUrl = repoInfo.AvatarUrl,
            IsPrivate = repoInfo.IsPrivate,
            GitUrl = $"https://github.com/{owner}/{repo}"
        };
    }

    private async Task<Repository?> GetRepositoryAsync(string owner, string repo)
    {
        return await context.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.OrgName == owner && item.RepoName == repo);
    }

    private async Task<RepositoryBranch?> GetBranchAsync(string repositoryId, string? branchName)
    {
        var branches = await context.RepositoryBranches
            .AsNoTracking()
            .Where(item => item.RepositoryId == repositoryId)
            .ToListAsync();

        if (branches.Count == 0)
        {
            return null;
        }

        // If a branch name is specified, try to find it
        if (!string.IsNullOrWhiteSpace(branchName))
        {
            var specified = branches.FirstOrDefault(item =>
                string.Equals(item.BranchName, branchName, StringComparison.OrdinalIgnoreCase));
            if (specified is not null)
            {
                return specified;
            }
        }

        // Otherwise return the default branch
        return branches.FirstOrDefault(item => string.Equals(item.BranchName, "main", StringComparison.OrdinalIgnoreCase))
               ?? branches.FirstOrDefault(item => string.Equals(item.BranchName, "master", StringComparison.OrdinalIgnoreCase))
               ?? branches.OrderBy(item => item.CreatedAt).First();
    }

    private async Task<BranchLanguage?> GetLanguageAsync(string branchId, string? languageCode)
    {
        var languages = await context.BranchLanguages
            .AsNoTracking()
            .Where(item => item.RepositoryBranchId == branchId)
            .ToListAsync();

        if (languages.Count == 0)
        {
            return null;
        }

        // If a language code is specified, try to find it
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            var specified = languages.FirstOrDefault(item =>
                string.Equals(item.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));
            if (specified is not null)
            {
                return specified;
            }
        }

        // Prefer returning the language marked as default
        var defaultLanguage = languages.FirstOrDefault(item => item.IsDefault);
        if (defaultLanguage is not null)
        {
            return defaultLanguage;
        }

        // Fallback: use the preset fallback language code
        return languages.FirstOrDefault(item => string.Equals(item.LanguageCode, FallbackLanguageCode, StringComparison.OrdinalIgnoreCase))
               ?? languages.OrderBy(item => item.CreatedAt).First();
    }

    private async Task<RepositoryBranch> GetDefaultBranchAsync(string repositoryId)
    {
        return await GetBranchAsync(repositoryId, null);
    }

    private async Task<BranchLanguage> GetDefaultLanguageAsync(string branchId)
    {
        return await GetLanguageAsync(branchId, null);
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Trim('/');
    }

    /// <summary>
    /// Recursively find the first document path with actual content
    /// </summary>
    private static string? FindFirstContentSlug(List<DocCatalog> catalogs, string? parentId)
    {
        var children = catalogs
            .Where(c => c.ParentId == parentId)
            .OrderBy(c => c.Order)
            .ToList();

        foreach (var child in children)
        {
            // If the current node has content, return its path
            if (!string.IsNullOrEmpty(child.DocFileId))
            {
                return NormalizePath(child.Path);
            }

            // Otherwise recursively search child nodes
            var childSlug = FindFirstContentSlug(catalogs, child.Id);
            if (childSlug != null)
            {
                return childSlug;
            }
        }

        return null;
    }

    private static RepositoryTreeNodeResponse BuildTreeNode(DocCatalog catalog, Dictionary<string, DocCatalog> catalogMap)
    {
        var node = new RepositoryTreeNodeResponse
        {
            Title = catalog.Title,
            Slug = NormalizePath(catalog.Path),
            Children = []
        };

        var children = catalogMap.Values
            .Where(c => c.ParentId == catalog.Id)
            .OrderBy(c => c.Order);

        foreach (var child in children)
        {
            node.Children.Add(BuildTreeNode(child, catalogMap));
        }

        return node;
    }

    /// <summary>
    /// Export repository documents as a compressed archive
    /// </summary>
    [HttpGet("/{owner}/{repo}/export")]
    [Authorize]
    public async Task<IActionResult> ExportAsync(string owner, string repo, [FromQuery] string? branch = null, [FromQuery] string? lang = null)
    {
        var repository = await GetRepositoryAsync(owner, repo);
        if (repository is null)
        {
            return new NotFoundObjectResult("Repository does not exist");
        }

        // Access control: restricted repos require ownership or department assignment
        if (!await CanAccessRepositoryAsync(repository))
        {
            return new UnauthorizedResult();
        }

        var branchEntity = await GetBranchAsync(repository.Id, branch);
        if (branchEntity is null)
        {
            return new NotFoundObjectResult("Branch does not exist");
        }

        var language = await GetLanguageAsync(branchEntity.Id, lang);
        if (language is null)
        {
            return new NotFoundObjectResult("Language does not exist");
        }

        var now = DateTime.UtcNow;
        var rateLimitKey = BuildRateLimitKey(owner, repo, branchEntity.BranchName, language.LanguageCode);

        var lastExportTime = await cache.GetAsync<DateTime?>(rateLimitKey);
        if (lastExportTime.HasValue)
        {
            var timeSinceLastExport = now - lastExportTime.Value;
            if (timeSinceLastExport.TotalMinutes < ExportRateLimitMinutes)
            {
                var remainingMinutes = Math.Ceiling(ExportRateLimitMinutes - timeSinceLastExport.TotalMinutes);
                return new BadRequestObjectResult($"Export rate limited, please try again in {remainingMinutes} minute(s)");
            }
        }

        if (!await TryAcquireExportSlotAsync())
        {
            return new BadRequestObjectResult("Too many concurrent export requests, please try again later");
        }

        try
        {
            await cache.SetAsync(rateLimitKey, now, new CacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ExportRateLimitTtl
            });

            // Get all document catalogs and files
            var catalogs = await context.DocCatalogs
                .AsNoTracking()
                .Where(item => item.BranchLanguageId == language.Id && !item.IsDeleted)
                .Include(item => item.DocFile)
                .OrderBy(item => item.Order)
                .ToListAsync();

            if (catalogs.Count == 0)
            {
                return new NotFoundObjectResult("No documents found for this branch and language");
            }

            // Create memory stream for generating compressed archive
            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                // Build directory structure and add files
                await AddFilesToArchive(archive, catalogs, null, null);
            }

            // Set file name
            var fileName = $"{owner}-{repo}-{branchEntity.BranchName}-{language.LanguageCode}.zip";
            
            // Return compressed archive
            return new FileContentResult(memoryStream.ToArray(), "application/zip")
            {
                FileDownloadName = fileName
            };
        }
        finally
        {
            await ReleaseExportSlotAsync();
        }
    }

    private static string BuildRateLimitKey(string owner, string repo, string branch, string language)
    {
        return $"{ExportRateLimitKeyPrefix}:{owner}:{repo}:{branch}:{language}";
    }

    private async Task<bool> TryAcquireExportSlotAsync()
    {
        await using var cacheLock = await cache.AcquireLockAsync(ExportConcurrencyLockKey, ExportConcurrencyLockTimeout);
        if (cacheLock is null)
        {
            return false;
        }

        var currentCount = await cache.GetAsync<int?>(ExportConcurrencyCountKey) ?? 0;
        if (currentCount >= MaxConcurrentExports)
        {
            return false;
        }

        await cache.SetAsync(ExportConcurrencyCountKey, currentCount + 1, new CacheEntryOptions
        {
            SlidingExpiration = ExportConcurrencyCountTtl
        });

        return true;
    }

    private async Task ReleaseExportSlotAsync()
    {
        await using var cacheLock = await cache.AcquireLockAsync(ExportConcurrencyLockKey, ExportConcurrencyLockTimeout);
        if (cacheLock is null)
        {
            return;
        }

        var currentCount = await cache.GetAsync<int?>(ExportConcurrencyCountKey) ?? 0;
        var nextCount = Math.Max(0, currentCount - 1);
        await cache.SetAsync(ExportConcurrencyCountKey, nextCount, new CacheEntryOptions
        {
            SlidingExpiration = ExportConcurrencyCountTtl
        });
    }

    /// <summary>
    /// Recursively add files to the compressed archive
    /// </summary>
    private static async Task AddFilesToArchive(
        ZipArchive archive,
        List<DocCatalog> catalogs,
        string? parentCatalogId,
        string? parentZipPath)
    {
        // Get directory items at the current level
        var currentLevelItems = catalogs
            .Where(c => c.ParentId == parentCatalogId)
            .OrderBy(c => c.Order)
            .ToList();

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var catalog in currentLevelItems)
        {
            var itemName = EnsureUniqueName(SanitizeZipNameSegment(catalog.Title), usedNames);

            // If there is a document file, create a file entry
            if (catalog.DocFile != null)
            {
                // Use .md extension
                var fileName = $"{itemName}.md";
                var fullPath = CombineZipPath(parentZipPath, fileName);

                var entry = archive.CreateEntry(fullPath);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                await writer.WriteAsync(catalog.DocFile.Content);
            }

            // Recursively process subdirectories
            var children = catalogs.Where(c => c.ParentId == catalog.Id).ToList();
            if (children.Count > 0)
            {
                var nextParentZipPath = CombineZipPath(parentZipPath, itemName);
                await AddFilesToArchive(archive, catalogs, catalog.Id, nextParentZipPath);
            }
        }
    }

    private static string CombineZipPath(string? parentZipPath, string entryName)
    {
        return string.IsNullOrEmpty(parentZipPath)
            ? entryName
            : $"{parentZipPath}/{entryName}";
    }

    private static string SanitizeZipNameSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "untitled";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());

        sanitized = sanitized.TrimEnd('.', ' ');

        return string.IsNullOrWhiteSpace(sanitized)
            ? "untitled"
            : sanitized;
    }

    private static string EnsureUniqueName(string baseName, ISet<string> usedNames)
    {
        if (usedNames.Add(baseName))
        {
            return baseName;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{baseName} ({index})";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Check if the current user can access a repository.
    /// Public repos are accessible to everyone.
    /// Restricted repos require authentication and either ownership or department assignment.
    /// </summary>
    private async Task<bool> CanAccessRepositoryAsync(Repository repository)
    {
        if (repository.IsPublic)
            return true;

        if (!userContext.IsAuthenticated || userContext.UserId is null)
            return false;

        // Owner always has access
        if (repository.OwnerUserId == userContext.UserId)
            return true;

        // Check department assignment
        var deptRepos = await organizationService.GetDepartmentRepositoriesAsync(userContext.UserId);
        return deptRepos.Any(r => r.OrgName == repository.OrgName && r.RepoName == repository.RepoName);
    }
}
