using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.Cache.Abstractions;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Infrastructure;
using OpenDeepWiki.Models;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using OpenDeepWiki.Services.Graphify;

namespace OpenDeepWiki.Services.Repositories;

[MiniApi(Route = "/api/v1/repos")]
[Tags("仓库文档")]
public class RepositoryDocsService(
    IContext context,
    IGitPlatformService gitPlatformService,
    ICache cache,
    IGraphifyArtifactService graphifyArtifactService,
    IRepositorySkillMarkdownBuilder skillMarkdownBuilder)
{
    private const string FallbackLanguageCode = "zh"; // 当没有默认语言标记时的回退语言
    private const int ExportRateLimitMinutes = 5; // 导出限流：5分钟内只能导出一次
    private static readonly TimeSpan GitPlatformCacheTtl = TimeSpan.FromMinutes(10); // Git 平台公开信息缓存，避免爬虫流量打爆外部 API 配额
    private const int MaxConcurrentExports = 10; // 最大并发导出数
    private const string ExportRateLimitKeyPrefix = "export:rate-limit";
    private const string ExportConcurrencyCountKey = "export:concurrency:count";
    private const string ExportConcurrencyLockKey = "export:concurrency:lock";
    private static readonly TimeSpan ExportConcurrencyLockTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExportConcurrencyCountTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ExportRateLimitTtl = TimeSpan.FromMinutes(ExportRateLimitMinutes);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    [HttpGet("/{owner}/{repo}/branches")]
    public async Task<RepositoryBranchesResponse> GetBranchesAsync(string owner, string repo)
    {
        (owner, repo) = RepositoryRouteDecoder.DecodeOwnerAndRepo(owner, repo);

        var version = await GetRepoCacheVersionAsync(owner, repo);
        var cacheKey = RepositoryPublicReadCache.BuildEntryKey("repos:branches", version, owner, repo, null, null);

        var cached = await cache.GetAsync<RepositoryBranchesResponse>(cacheKey);
        if (cached is not null)
        {
            return cached;
        }

        var (response, isCacheable, isStableState) = await GetBranchesCoreAsync(owner, repo);

        if (isCacheable)
        {
            await cache.SetAsync(cacheKey, response, new CacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = isStableState
                    ? RepositoryPublicReadCache.StableTtl
                    : RepositoryPublicReadCache.VolatileTtl
            });
        }

        return response;
    }

    private async Task<(RepositoryBranchesResponse Response, bool IsCacheable, bool IsStableState)> GetBranchesCoreAsync(string owner, string repo)
    {
        var repository = await GetRepositoryAsync(owner, repo);

        if (repository is null)
        {
            // 仓库不存在：缓存短 TTL，防止爬虫扫描不存在路径造成穿透
            return (new RepositoryBranchesResponse { Branches = [], Languages = [] }, IsCacheable: true, IsStableState: false);
        }

        var isStableState = repository.Status == RepositoryStatus.Completed;
        var isCacheable = repository.IsPublic;

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
            // 获取该分支下有实际文档内容的语言
            var languagesWithContent = await context.BranchLanguages
                .AsNoTracking()
                .Where(item => item.RepositoryBranchId == branch.Id)
                .Where(item => context.DocCatalogs.Any(c =>
                    c.BranchLanguageId == item.Id &&
                    !c.IsDeleted &&
                    !string.IsNullOrEmpty(c.DocFileId) &&
                    !context.DocCatalogs.Any(child =>
                        child.BranchLanguageId == item.Id &&
                        child.ParentId == c.Id &&
                        !child.IsDeleted)))
                .ToListAsync();

            var hasActiveOrFailedGeneration = branch.GenerationStatus is BranchGenerationTaskStatus.Pending
                or BranchGenerationTaskStatus.Processing
                or BranchGenerationTaskStatus.Failed;

            if (languagesWithContent.Count > 0 || hasActiveOrFailedGeneration)
            {
                branchItems.Add(new BranchItem
                {
                    Id = branch.Id,
                    Name = branch.BranchName,
                    Languages = languagesWithContent.Select(l => l.LanguageCode).ToList(),
                    GenerationStatus = branch.GenerationStatus?.ToString(),
                    LastGenerationTaskId = branch.LastGenerationTaskId,
                    LastGenerationError = branch.LastGenerationError,
                    LastGenerationStartedAt = branch.LastGenerationStartedAt,
                    LastGenerationCompletedAt = branch.LastGenerationCompletedAt
                });

                foreach (var lang in languagesWithContent)
                {
                    allLanguages.Add(lang.LanguageCode);
                    // 记录标记为默认的语言
                    if (lang.IsDefault && defaultLanguageCode is null)
                    {
                        defaultLanguageCode = lang.LanguageCode;
                    }
                }
            }
        }

        // 确定默认分支（只从有内容的分支中选择）
        var defaultBranch = branchItems.FirstOrDefault(b => 
            string.Equals(b.Name, "main", StringComparison.OrdinalIgnoreCase))?.Name
            ?? branchItems.FirstOrDefault(b => 
                string.Equals(b.Name, "master", StringComparison.OrdinalIgnoreCase))?.Name
            ?? branchItems.FirstOrDefault()?.Name
            ?? "";

        // 确定默认语言：优先使用标记的默认语言，否则回退
        var finalDefaultLanguage = defaultLanguageCode 
            ?? (allLanguages.Contains(FallbackLanguageCode) ? FallbackLanguageCode : allLanguages.FirstOrDefault() ?? "");

        return (new RepositoryBranchesResponse
        {
            RepositoryId = repository.Id,
            Branches = branchItems,
            Languages = allLanguages.ToList(),
            DefaultBranch = defaultBranch,
            DefaultLanguage = finalDefaultLanguage
        }, isCacheable, isStableState);
    }

    private async Task<string> GetRepoCacheVersionAsync(string owner, string repo)
    {
        var key = RepositoryPublicReadCache.BuildVersionKey(owner, repo);
        var version = await cache.GetAsync<string>(key);
        if (!string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        version = Guid.NewGuid().ToString("N");
        await cache.SetAsync(key, version, new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = RepositoryPublicReadCache.VersionTtl
        });
        return version;
    }

    [HttpGet("/{owner}/{repo}/tree")]
    public async Task<RepositoryTreeResponse> GetTreeAsync(string owner, string repo, [FromQuery] string? branch = null, [FromQuery] string? lang = null)
    {
        (owner, repo) = RepositoryRouteDecoder.DecodeOwnerAndRepo(owner, repo);

        var version = await GetRepoCacheVersionAsync(owner, repo);
        var cacheKey = RepositoryPublicReadCache.BuildEntryKey("repos:tree", version, owner, repo, branch, lang);

        var cached = await cache.GetAsync<RepositoryTreeResponse>(cacheKey);
        if (cached is not null)
        {
            return cached;
        }

        var (response, isCacheable, isStableState) = await GetTreeCoreAsync(owner, repo, branch, lang);

        if (isCacheable)
        {
            await cache.SetAsync(cacheKey, response, new CacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = isStableState
                    ? RepositoryPublicReadCache.StableTtl
                    : RepositoryPublicReadCache.VolatileTtl
            });
        }

        return response;
    }

    private async Task<(RepositoryTreeResponse Response, bool IsCacheable, bool IsStableState)> GetTreeCoreAsync(string owner, string repo, string? branch, string? lang)
    {
        var repository = await GetRepositoryAsync(owner, repo);

        // 仓库不存在
        if (repository is null)
        {
            // 缓存短 TTL，防止爬虫扫描不存在路径造成缓存穿透
            return (new RepositoryTreeResponse
            {
                Owner = owner,
                Repo = repo,
                Exists = false,
                Status = RepositoryStatus.Pending,
                Nodes = []
            }, IsCacheable: true, IsStableState: false);
        }

        var isStableState = repository.Status == RepositoryStatus.Completed;
        var isCacheable = repository.IsPublic;

        // 仓库正在处理中或等待处理
        if (repository.Status == RepositoryStatus.Pending || repository.Status == RepositoryStatus.Processing)
        {
            return (new RepositoryTreeResponse
            {
                Owner = repository.OrgName,
                Repo = repository.RepoName,
                Exists = true,
                Status = repository.Status,
                Nodes = []
            }, isCacheable, isStableState);
        }

        // 仓库处理完成或失败，获取文档目录
        var branchEntity = await GetBranchAsync(repository.Id, branch);
        if (branchEntity is null)
        {
            return (new RepositoryTreeResponse
            {
                Owner = repository.OrgName,
                Repo = repository.RepoName,
                Exists = true,
                Status = repository.Status,
                Nodes = []
            }, isCacheable, isStableState);
        }

        var language = await GetLanguageAsync(branchEntity.Id, lang);
        var graphifyState = await GetGraphifyStateAsync(branchEntity.Id);
        if (language is null)
        {
            return (new RepositoryTreeResponse
            {
                Owner = repository.OrgName,
                Repo = repository.RepoName,
                Exists = true,
                Status = repository.Status,
                CurrentBranch = branchEntity.BranchName,
                HasGraphifyArtifact = graphifyState.HasArtifact,
                GraphifyStatus = graphifyState.Status,
                GraphifyStatusName = graphifyState.StatusName,
                Nodes = []
            }, isCacheable, isStableState);
        }

        var catalogs = await context.DocCatalogs
            .AsNoTracking()
            .Where(item => item.BranchLanguageId == language.Id && !item.IsDeleted)
            .OrderBy(item => item.Order)
            .ToListAsync();

        if (catalogs.Count == 0)
        {
            // 仓库已完成但没有文档，可能是空仓库
            return (new RepositoryTreeResponse
            {
                Owner = repository.OrgName,
                Repo = repository.RepoName,
                Exists = true,
                Status = repository.Status,
                CurrentBranch = branchEntity.BranchName,
                CurrentLanguage = language.LanguageCode,
                HasGraphifyArtifact = graphifyState.HasArtifact,
                GraphifyStatus = graphifyState.Status,
                GraphifyStatusName = graphifyState.StatusName,
                Nodes = []
            }, isCacheable, isStableState);
        }

        // 构建树形结构
        var readyCatalogs = FilterToReadyWithAncestors(catalogs);
        if (readyCatalogs.Count == 0)
        {
            return (new RepositoryTreeResponse
            {
                Owner = repository.OrgName,
                Repo = repository.RepoName,
                Exists = true,
                Status = repository.Status,
                CurrentBranch = branchEntity.BranchName,
                CurrentLanguage = language.LanguageCode,
                HasGraphifyArtifact = graphifyState.HasArtifact,
                GraphifyStatus = graphifyState.Status,
                GraphifyStatusName = graphifyState.StatusName,
                Nodes = []
            }, isCacheable, isStableState);
        }

        var catalogMap = readyCatalogs.ToDictionary(c => c.Id);
        var rootNodes = new List<RepositoryTreeNodeResponse>();

        foreach (var catalog in readyCatalogs.Where(c => c.ParentId == null))
        {
            rootNodes.Add(BuildTreeNode(catalog, catalogMap));
        }

        // 递归查找第一个有实际内容的文档
        var defaultSlug = FindFirstContentSlug(readyCatalogs, null) ?? string.Empty;

        return (new RepositoryTreeResponse
        {
            Owner = repository.OrgName,
            Repo = repository.RepoName,
            DefaultSlug = defaultSlug,
            Nodes = rootNodes,
            Exists = true,
            Status = repository.Status,
            CurrentBranch = branchEntity.BranchName,
            CurrentLanguage = language.LanguageCode,
            HasGraphifyArtifact = graphifyState.HasArtifact,
            GraphifyStatus = graphifyState.Status,
            GraphifyStatusName = graphifyState.StatusName
        }, isCacheable, isStableState);
    }

    [HttpGet("/{owner}/{repo}/graphify")]
    public async Task<IResult> GetGraphifyAsync(
        string owner,
        string repo,
        [FromQuery] string? branch = null)
    {
        (owner, repo) = RepositoryRouteDecoder.DecodeOwnerAndRepo(owner, repo);

        var artifact = await graphifyArtifactService.GetCompletedArtifactAsync(owner, repo, branch);
        if (artifact == null || string.IsNullOrWhiteSpace(artifact.EntryFilePath))
        {
            return Results.NotFound("Graphify artifact not found");
        }

        var entryPath = Path.GetFullPath(artifact.EntryFilePath);
        if (!System.IO.File.Exists(entryPath))
        {
            return Results.NotFound("Graphify HTML file not found");
        }

        return Results.File(entryPath, "text/html; charset=utf-8");
    }

    [HttpGet("/{owner}/{repo}/graphify/report")]
    public async Task<IResult> GetGraphifyReportAsync(
        string owner,
        string repo,
        [FromQuery] string? branch = null)
    {
        (owner, repo) = RepositoryRouteDecoder.DecodeOwnerAndRepo(owner, repo);

        var artifact = await graphifyArtifactService.GetCompletedArtifactAsync(owner, repo, branch);
        if (artifact == null || string.IsNullOrWhiteSpace(artifact.ReportPath))
        {
            return Results.NotFound("Graphify report not found");
        }

        var reportPath = Path.GetFullPath(artifact.ReportPath);
        if (!System.IO.File.Exists(reportPath))
        {
            return Results.NotFound("Graphify report file not found");
        }

        return Results.File(reportPath, "text/markdown; charset=utf-8");
    }

    /// <summary>
    /// 仅保留已经生成内容的文档及其全部祖先目录节点。
    /// 用于在仓库生成过程中返回可浏览的部分文档树。
    /// </summary>
    private static List<DocCatalog> FilterToReadyWithAncestors(List<DocCatalog> catalogs)
    {
        var byId = catalogs.ToDictionary(c => c.Id);
        var keep = new HashSet<string>();

        foreach (var catalog in catalogs)
        {
            var hasChildren = catalogs.Any(child => child.ParentId == catalog.Id);
            if (hasChildren || string.IsNullOrEmpty(catalog.DocFileId))
            {
                continue;
            }

            var current = catalog;
            while (current is not null && keep.Add(current.Id))
            {
                if (current.ParentId is null || !byId.TryGetValue(current.ParentId, out var parent))
                {
                    break;
                }
                current = parent;
            }
        }

        return catalogs
            .Where(c => keep.Contains(c.Id))
            .OrderBy(c => c.Order)
            .ToList();
    }

    private async Task<(bool HasArtifact, int? Status, string? StatusName)> GetGraphifyStateAsync(
        string repositoryBranchId)
    {
        var artifact = await context.GraphifyArtifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.RepositoryBranchId == repositoryBranchId && !a.IsDeleted);

        if (artifact == null)
        {
            return (false, null, null);
        }

        var hasArtifact = artifact.Status == GraphifyArtifactStatus.Completed &&
                          !string.IsNullOrWhiteSpace(artifact.EntryFilePath) &&
                          System.IO.File.Exists(artifact.EntryFilePath);

        return (hasArtifact, (int)artifact.Status, artifact.Status.ToString());
    }

    [HttpGet("/{owner}/{repo}/docs/{*slug}")]
    public async Task<RepositoryDocResponse> GetDocAsync(string owner, string repo, string slug, [FromQuery] string? branch = null, [FromQuery] string? lang = null)
    {
        (owner, repo) = RepositoryRouteDecoder.DecodeOwnerAndRepo(owner, repo);
        var normalizedSlug = NormalizePath(slug);

        var version = await GetRepoCacheVersionAsync(owner, repo);
        var cacheKey = RepositoryPublicReadCache.BuildEntryKey($"repos:doc:{normalizedSlug}", version, owner, repo, branch, lang);

        var cached = await cache.GetAsync<RepositoryDocResponse>(cacheKey);
        if (cached is not null)
        {
            return cached;
        }

        var (response, isCacheable, isStableState) = await GetDocCoreAsync(owner, repo, normalizedSlug, branch, lang);

        if (isCacheable)
        {
            await cache.SetAsync(cacheKey, response, new CacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = isStableState
                    ? RepositoryPublicReadCache.StableTtl
                    : RepositoryPublicReadCache.VolatileTtl
            });
        }

        return response;
    }

    private async Task<(RepositoryDocResponse Response, bool IsCacheable, bool IsStableState)> GetDocCoreAsync(string owner, string repo, string normalizedSlug, string? branch, string? lang)
    {
        var repository = await GetRepositoryAsync(owner, repo);
        if (repository is null)
        {
            // 缓存短 TTL，防止爬虫扫描不存在路径造成缓存穿透
            return (new RepositoryDocResponse { Slug = normalizedSlug, Exists = false }, IsCacheable: true, IsStableState: false);
        }

        var isCacheable = repository.IsPublic;
        var isStableState = repository.Status == RepositoryStatus.Completed;

        var branchEntity = await GetBranchAsync(repository.Id, branch);
        if (branchEntity is null)
        {
            return (new RepositoryDocResponse { Slug = normalizedSlug, Exists = false }, isCacheable, isStableState);
        }

        var language = await GetLanguageAsync(branchEntity.Id, lang);
        if (language is null)
        {
            return (new RepositoryDocResponse { Slug = normalizedSlug, Exists = false }, isCacheable, isStableState);
        }

        var catalog = await context.DocCatalogs
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.BranchLanguageId == language.Id && item.Path == normalizedSlug && !item.IsDeleted);

        if (catalog is null)
        {
            return (new RepositoryDocResponse { Slug = normalizedSlug, Exists = false }, isCacheable, isStableState);
        }

        var hasChildren = await context.DocCatalogs
            .AsNoTracking()
            .AnyAsync(item => item.BranchLanguageId == language.Id && item.ParentId == catalog.Id && !item.IsDeleted);

        if (hasChildren || string.IsNullOrEmpty(catalog.DocFileId))
        {
            return (new RepositoryDocResponse { Slug = normalizedSlug, Exists = false }, isCacheable, isStableState);
        }

        var docFile = await context.DocFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == catalog.DocFileId);

        if (docFile is null)
        {
            return (new RepositoryDocResponse { Slug = normalizedSlug, Exists = false }, isCacheable, isStableState);
        }

        // 解析来源文件列表
        var sourceFiles = new List<string>();
        if (!string.IsNullOrEmpty(docFile.SourceFiles))
        {
            try
            {
                sourceFiles = System.Text.Json.JsonSerializer.Deserialize<List<string>>(docFile.SourceFiles) ?? [];
            }
            catch
            {
                // 解析失败时返回空列表
            }
        }

        return (new RepositoryDocResponse
        {
            Slug = normalizedSlug,
            Content = docFile.Content,
            SourceFiles = sourceFiles,
            GitUrl = repository.GitUrl,
            Branch = branchEntity.BranchName,
            Exists = true
        }, isCacheable, isStableState);
    }

    /// <summary>
    /// 检查GitHub仓库是否存在
    /// </summary>
    [HttpGet("/{owner}/{repo}/check")]
    public async Task<GitRepoCheckResponse> CheckRepoAsync(string owner, string repo)
    {
        (owner, repo) = RepositoryRouteDecoder.DecodeOwnerAndRepo(owner, repo);

        // 结果来自 Git 平台公开 API，有外部配额限制，必须缓存：
        // 爬虫扫描大量无效 owner/repo 时会在此处打爆 GitHub/Gitee 配额
        var cacheKey = $"repos:gitcheck:{owner.ToLowerInvariant()}:{repo.ToLowerInvariant()}";
        var cached = await cache.GetAsync<GitRepoCheckResponse>(cacheKey);
        if (cached is not null)
        {
            return cached;
        }

        var repoInfo = await gitPlatformService.CheckRepoExistsAsync(owner, repo);

        var response = new GitRepoCheckResponse
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

        await cache.SetAsync(cacheKey, response, new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = GitPlatformCacheTtl
        });

        return response;
    }

    private async Task<Repository?> GetRepositoryAsync(string owner, string repo)
    {
        var repository = await context.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.OrgName == owner && item.RepoName == repo && !item.IsDeleted);

        if (repository is not null)
        {
            return repository;
        }

        var normalizedOwner = owner.ToLower();
        var normalizedRepo = repo.ToLower();

        return await context.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.OrgName.ToLower() == normalizedOwner &&
                item.RepoName.ToLower() == normalizedRepo &&
                !item.IsDeleted);
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

        // 如果指定了分支名，尝试查找
        if (!string.IsNullOrWhiteSpace(branchName))
        {
            var specified = branches.FirstOrDefault(item =>
                string.Equals(item.BranchName, branchName, StringComparison.OrdinalIgnoreCase));
            if (specified is not null)
            {
                return specified;
            }
        }

        // 否则返回默认分支
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

        // 如果指定了语言代码，尝试查找
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            var specified = languages.FirstOrDefault(item =>
                string.Equals(item.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));
            if (specified is not null)
            {
                return specified;
            }
        }

        // 优先返回标记为默认的语言
        var defaultLanguage = languages.FirstOrDefault(item => item.IsDefault);
        if (defaultLanguage is not null)
        {
            return defaultLanguage;
        }

        // 回退：使用预设的回退语言代码
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
        return System.Net.WebUtility.UrlDecode(path.Trim().Trim('/'));
    }

    /// <summary>
    /// 递归查找第一个有实际内容的文档路径
    /// </summary>
    private static string? FindFirstContentSlug(List<DocCatalog> catalogs, string? parentId)
    {
        var children = catalogs
            .Where(c => c.ParentId == parentId)
            .OrderBy(c => c.Order)
            .ToList();

        foreach (var child in children)
        {
            var hasChildren = catalogs.Any(c => c.ParentId == child.Id);

            // 如果当前节点有内容，返回它的路径
            if (!hasChildren && !string.IsNullOrEmpty(child.DocFileId))
            {
                return NormalizePath(child.Path);
            }

            // 否则递归查找子节点
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
    /// 导出仓库文档为压缩包
    /// </summary>
    [HttpGet("/{owner}/{repo}/export")]
    [Authorize]
    public async Task<IActionResult> ExportAsync(string owner, string repo, [FromQuery] string? branch = null, [FromQuery] string? lang = null)
    {
        (owner, repo) = RepositoryRouteDecoder.DecodeOwnerAndRepo(owner, repo);
        var repository = await GetRepositoryAsync(owner, repo);
        if (repository is null)
        {
            return new NotFoundObjectResult("仓库不存在");
        }

        var branchEntity = await GetBranchAsync(repository.Id, branch);
        if (branchEntity is null)
        {
            return new NotFoundObjectResult("分支不存在");
        }

        var language = await GetLanguageAsync(branchEntity.Id, lang);
        if (language is null)
        {
            return new NotFoundObjectResult("语言不存在");
        }

        if (!repository.GenerateSkill)
        {
            return new BadRequestObjectResult("This repository does not have SKILL export enabled.");
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
                return new BadRequestObjectResult($"导出过于频繁，请在 {remainingMinutes} 分钟后重试");
            }
        }

        if (!await TryAcquireExportSlotAsync())
        {
            return new BadRequestObjectResult("当前导出请求过多，请稍后重试");
        }

        try
        {
            await cache.SetAsync(rateLimitKey, now, new CacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ExportRateLimitTtl
            });

            // 获取所有文档目录和文件
            var catalogs = await context.DocCatalogs
                .AsNoTracking()
                .Where(item => item.BranchLanguageId == language.Id && !item.IsDeleted)
                .Include(item => item.DocFile)
                .OrderBy(item => item.Order)
                .ToListAsync();

            if (catalogs.Count == 0)
            {
                return new NotFoundObjectResult("该分支和语言下没有文档内容");
            }

            // 创建内存流用于生成压缩包
            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                var skillMarkdown = string.IsNullOrWhiteSpace(language.SkillMarkdown)
                    ? skillMarkdownBuilder.BuildSkillMarkdown(repository, branchEntity, language, catalogs, now)
                    : language.SkillMarkdown;

                var skillEntry = archive.CreateEntry("SKILL.md");
                await using (var skillEntryStream = skillEntry.Open())
                await using (var writer = new StreamWriter(skillEntryStream, Utf8NoBom))
                {
                    await writer.WriteAsync(skillMarkdown);
                }

                await skillMarkdownBuilder.AddDocumentsToArchiveAsync(
                    archive,
                    catalogs,
                    "references/docs");
            }

            // 设置文件名
            var fileName = $"{SanitizeDownloadFileName(owner)}-{SanitizeDownloadFileName(repo)}-{SanitizeDownloadFileName(branchEntity.BranchName)}-{SanitizeDownloadFileName(language.LanguageCode)}-skill.zip";

            // 重置内存流并获取字节数组
            memoryStream.Seek(0, SeekOrigin.Begin);
            var zipBytes = memoryStream.ToArray();

            // 返回压缩包
            return new FileContentResult(zipBytes, "application/zip") { FileDownloadName = fileName };
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

    private static string SanitizeDownloadFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(new[] { '/', '\\' })
            .ToHashSet();
        var sanitized = new string(value
            .Select(ch => invalidChars.Contains(ch) ? '-' : ch)
            .ToArray())
            .Trim('-', '.', ' ');

        return string.IsNullOrWhiteSpace(sanitized)
            ? "repository"
            : sanitized;
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
    /// 递归添加文件到压缩包
    /// </summary>
    private static async Task AddFilesToArchive(
        ZipArchive archive,
        List<DocCatalog> catalogs,
        string? parentCatalogId,
        string? parentZipPath)
    {
        // 获取当前层级的目录项
        var currentLevelItems = catalogs
            .Where(c => c.ParentId == parentCatalogId)
            .OrderBy(c => c.Order)
            .ToList();

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var catalog in currentLevelItems)
        {
            var itemName = EnsureUniqueName(SanitizeZipNameSegment(catalog.Title), usedNames);
            var children = catalogs.Where(c => c.ParentId == catalog.Id).ToList();

            // 如果有文档文件，创建文件条目
            if (catalog.DocFile != null && children.Count == 0)
            {
                // 使用 .md 扩展名
                var fileName = $"{itemName}.md";
                var fullPath = CombineZipPath(parentZipPath, fileName);

                var entry = archive.CreateEntry(fullPath);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                await writer.WriteAsync(catalog.DocFile.Content);
            }

            // 递归处理子目录
            if (children.Count > 0)
            {
                // 如果没有文档文件但有子项，创建 ZIP 目录条目
                var dirPath = CombineZipPath(parentZipPath, itemName);
                archive.CreateEntry(dirPath.EndsWith('/') ? dirPath : dirPath + '/');

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
}
