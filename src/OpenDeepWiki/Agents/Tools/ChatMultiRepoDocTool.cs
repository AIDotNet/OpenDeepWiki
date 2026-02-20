using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenDeepWiki.EFCore;

namespace OpenDeepWiki.Agents.Tools;

/// <summary>
/// AI Tool for reading documentation from any accessible repository.
/// Supports permission-aware filtering: if a DeepWiki user ID is provided,
/// shows public repos + private repos assigned via departments.
/// Otherwise shows only public repos.
/// </summary>
public class ChatMultiRepoDocTool
{
    private readonly IContextFactory _contextFactory;
    private readonly string _repositorySummary;
    private readonly string? _deepWikiUserId;

    public ChatMultiRepoDocTool(IContextFactory contextFactory, string repositorySummary, string? deepWikiUserId = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _repositorySummary = repositorySummary ?? string.Empty;
        _deepWikiUserId = deepWikiUserId;
    }

    /// <summary>
    /// Lists all accessible repositories with their documentation status.
    /// </summary>
    [Description("List all repositories you have access to in DeepWiki with their branches and languages.")]
    public async Task<string> ListRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = _contextFactory.CreateContext();

            var accessibleRepoIds = await GetAccessibleRepoIdsAsync(context, cancellationToken);

            var query = context.Repositories
                .Where(r => !r.IsDeleted &&
                    (r.Status == Entities.RepositoryStatus.Processing ||
                     r.Status == Entities.RepositoryStatus.Completed));

            if (accessibleRepoIds != null)
            {
                // User is mapped: show public + department-assigned repos
                query = query.Where(r => r.IsPublic || accessibleRepoIds.Contains(r.Id));
            }
            else
            {
                // User not mapped: public repos only
                query = query.Where(r => r.IsPublic);
            }

            var repos = await query
                .Select(r => new
                {
                    r.Id,
                    r.OrgName,
                    r.RepoName,
                    r.PrimaryLanguage,
                    r.GitUrl,
                    r.Status,
                    r.IsPublic
                })
                .ToListAsync(cancellationToken);

            if (repos.Count == 0)
            {
                return JsonSerializer.Serialize(new { repositories = Array.Empty<object>(), message = "No accessible repositories are currently indexed." });
            }

            var result = repos.Select(r => new
            {
                owner = r.OrgName,
                repo = r.RepoName,
                primaryLanguage = r.PrimaryLanguage,
                url = r.GitUrl,
                status = r.Status == Entities.RepositoryStatus.Completed ? "ready" : "indexing",
                visibility = r.IsPublic ? "public" : "private"
            });

            return JsonSerializer.Serialize(new { repositories = result, count = repos.Count });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = true, message = $"Failed to list repositories: {ex.Message}" });
        }
    }

    /// <summary>
    /// Lists documentation catalog for a specific repository.
    /// </summary>
    [Description("List the documentation catalog (table of contents) for a specific repository. Use this to discover available documents before reading them.")]
    public async Task<string> ListDocumentsAsync(
        [Description("Repository owner/organization (e.g. 'keboola')")] string owner,
        [Description("Repository name (e.g. 'go-client')")] string repo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            return JsonSerializer.Serialize(new { error = true, message = "Owner and repo are required." });
        }

        try
        {
            using var context = _contextFactory.CreateContext();

            var repository = await context.Repositories
                .FirstOrDefaultAsync(r => r.OrgName == owner && r.RepoName == repo && !r.IsDeleted, cancellationToken);

            if (repository == null)
            {
                return JsonSerializer.Serialize(new { error = true, message = $"Repository '{owner}/{repo}' not found." });
            }

            // Check access permission
            if (!await CanAccessRepositoryAsync(context, repository.Id, repository.IsPublic, cancellationToken))
            {
                return JsonSerializer.Serialize(new { error = true, message = $"You don't have access to repository '{owner}/{repo}'." });
            }

            // Get the first available branch
            var branch = await context.RepositoryBranches
                .FirstOrDefaultAsync(b => b.RepositoryId == repository.Id && !b.IsDeleted, cancellationToken);

            if (branch == null)
            {
                return JsonSerializer.Serialize(new { error = true, message = $"No branches found for '{owner}/{repo}'." });
            }

            // Prefer English, fall back to first available language
            var branchLanguage = await context.BranchLanguages
                .Where(bl => bl.RepositoryBranchId == branch.Id && !bl.IsDeleted)
                .OrderByDescending(bl => bl.LanguageCode == "en")
                .FirstOrDefaultAsync(cancellationToken);

            if (branchLanguage == null)
            {
                return JsonSerializer.Serialize(new { error = true, message = $"No documentation languages found for '{owner}/{repo}'." });
            }

            var catalogs = await context.DocCatalogs
                .Where(c => c.BranchLanguageId == branchLanguage.Id && !c.IsDeleted && !string.IsNullOrEmpty(c.DocFileId))
                .OrderBy(c => c.Order)
                .Select(c => new { c.Title, c.Path })
                .ToListAsync(cancellationToken);

            if (catalogs.Count == 0)
            {
                return JsonSerializer.Serialize(new { error = true, message = $"No documents found for '{owner}/{repo}'." });
            }

            return JsonSerializer.Serialize(new
            {
                repository = $"{owner}/{repo}",
                branch = branch.BranchName,
                language = branchLanguage.LanguageCode,
                documents = catalogs,
                count = catalogs.Count
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = true, message = $"Failed to list documents: {ex.Message}" });
        }
    }

    /// <summary>
    /// Reads document content from a specific repository.
    /// </summary>
    [Description("Read documentation content from a specific repository. Use ListDocuments first to discover available document paths.")]
    public async Task<string> ReadDocAsync(
        [Description("Repository owner/organization (e.g. 'keboola')")] string owner,
        [Description("Repository name (e.g. 'go-client')")] string repo,
        [Description("Document path from the catalog (e.g. 'overview')")] string path,
        [Description("Starting line number (1-based, inclusive)")] int startLine,
        [Description("Ending line number (1-based, inclusive, max 200 lines per request)")] int endLine,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            return JsonSerializer.Serialize(new { error = true, message = "Owner and repo are required." });
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return JsonSerializer.Serialize(new { error = true, message = "Document path is required." });
        }

        if (startLine < 1)
        {
            return JsonSerializer.Serialize(new { error = true, message = "startLine must be >= 1." });
        }

        if (endLine < startLine)
        {
            return JsonSerializer.Serialize(new { error = true, message = "endLine must be >= startLine." });
        }

        if (endLine - startLine > 200)
        {
            return JsonSerializer.Serialize(new { error = true, message = "Maximum 200 lines per request. Narrow your range." });
        }

        try
        {
            using var context = _contextFactory.CreateContext();

            var repository = await context.Repositories
                .FirstOrDefaultAsync(r => r.OrgName == owner && r.RepoName == repo && !r.IsDeleted, cancellationToken);

            if (repository == null)
            {
                return JsonSerializer.Serialize(new { error = true, message = $"Repository '{owner}/{repo}' not found." });
            }

            // Check access permission
            if (!await CanAccessRepositoryAsync(context, repository.Id, repository.IsPublic, cancellationToken))
            {
                return JsonSerializer.Serialize(new { error = true, message = $"You don't have access to repository '{owner}/{repo}'." });
            }

            var branch = await context.RepositoryBranches
                .FirstOrDefaultAsync(b => b.RepositoryId == repository.Id && !b.IsDeleted, cancellationToken);

            if (branch == null)
            {
                return JsonSerializer.Serialize(new { error = true, message = $"No branches found for '{owner}/{repo}'." });
            }

            // Prefer English, fall back to first available language
            var branchLanguage = await context.BranchLanguages
                .Where(bl => bl.RepositoryBranchId == branch.Id && !bl.IsDeleted)
                .OrderByDescending(bl => bl.LanguageCode == "en")
                .FirstOrDefaultAsync(cancellationToken);

            if (branchLanguage == null)
            {
                return JsonSerializer.Serialize(new { error = true, message = $"No documentation found for '{owner}/{repo}'." });
            }

            var catalog = await context.DocCatalogs
                .FirstOrDefaultAsync(c => c.BranchLanguageId == branchLanguage.Id && c.Path == path && !c.IsDeleted, cancellationToken);

            if (catalog == null)
            {
                return JsonSerializer.Serialize(new { error = true, message = $"Document '{path}' not found in '{owner}/{repo}'." });
            }

            if (string.IsNullOrEmpty(catalog.DocFileId))
            {
                return JsonSerializer.Serialize(new { error = true, message = $"Document '{path}' has no content." });
            }

            var docFile = await context.DocFiles
                .FirstOrDefaultAsync(d => d.Id == catalog.DocFileId && !d.IsDeleted, cancellationToken);

            if (docFile == null)
            {
                return JsonSerializer.Serialize(new { error = true, message = $"Document content not found for '{path}'." });
            }

            var allLines = docFile.Content.Split('\n');
            var totalLines = allLines.Length;
            var actualEndLine = Math.Min(endLine, totalLines);

            if (startLine > totalLines)
            {
                return JsonSerializer.Serialize(new
                {
                    error = true,
                    message = $"startLine ({startLine}) exceeds total lines ({totalLines})."
                });
            }

            var selectedLines = allLines.Skip(startLine - 1).Take(actualEndLine - startLine + 1);
            var content = string.Join("\n", selectedLines);

            List<string>? sourceFiles = null;
            if (!string.IsNullOrEmpty(docFile.SourceFiles))
            {
                try
                {
                    sourceFiles = JsonSerializer.Deserialize<List<string>>(docFile.SourceFiles);
                }
                catch
                {
                    // Ignore parse failures
                }
            }

            return JsonSerializer.Serialize(new
            {
                content,
                repository = $"{owner}/{repo}",
                path,
                startLine,
                endLine = actualEndLine,
                totalLines,
                sourceFiles
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = true, message = $"Failed to read document: {ex.Message}" });
        }
    }

    /// <summary>
    /// Gets all AI tools provided by this class.
    /// </summary>
    public IList<AITool> GetTools()
    {
        var tools = new List<AITool>();

        tools.Add(AIFunctionFactory.Create(ListRepositoriesAsync, new AIFunctionFactoryOptions
        {
            Name = "ListRepositories",
            Description = BuildListReposDescription()
        }));

        tools.Add(AIFunctionFactory.Create(ListDocumentsAsync, new AIFunctionFactoryOptions
        {
            Name = "ListDocuments",
            Description = "List the documentation catalog (table of contents) for a specific repository. Use this to discover available document paths before reading them with ReadDoc."
        }));

        tools.Add(AIFunctionFactory.Create(ReadDocAsync, new AIFunctionFactoryOptions
        {
            Name = "ReadDoc",
            Description = "Read documentation content from a repository. Specify owner, repo, document path, and line range. Use ListDocuments first to find available paths. Maximum 200 lines per request."
        }));

        return tools;
    }

    private string BuildListReposDescription()
    {
        var sb = new StringBuilder();
        sb.AppendLine("List all repositories you have access to in DeepWiki.");
        if (!string.IsNullOrEmpty(_repositorySummary))
        {
            sb.AppendLine();
            sb.AppendLine("Currently accessible repositories:");
            sb.AppendLine(_repositorySummary);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Creates a ChatMultiRepoDocTool with a pre-loaded repository summary.
    /// </summary>
    public static async Task<ChatMultiRepoDocTool> CreateAsync(
        IContextFactory contextFactory,
        string? deepWikiUserId = null,
        CancellationToken cancellationToken = default)
    {
        var summary = await LoadRepositorySummaryAsync(contextFactory, deepWikiUserId, cancellationToken);
        return new ChatMultiRepoDocTool(contextFactory, summary, deepWikiUserId);
    }

    private static async Task<string> LoadRepositorySummaryAsync(
        IContextFactory contextFactory,
        string? deepWikiUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var context = contextFactory.CreateContext();

            HashSet<string>? accessibleRepoIds = null;
            if (!string.IsNullOrEmpty(deepWikiUserId))
            {
                accessibleRepoIds = await GetAccessibleRepoIdsStaticAsync(context, deepWikiUserId, cancellationToken);
            }

            var query = context.Repositories
                .Where(r => !r.IsDeleted &&
                    (r.Status == Entities.RepositoryStatus.Processing ||
                     r.Status == Entities.RepositoryStatus.Completed));

            if (accessibleRepoIds != null)
            {
                query = query.Where(r => r.IsPublic || accessibleRepoIds.Contains(r.Id));
            }
            else
            {
                query = query.Where(r => r.IsPublic);
            }

            var repos = await query
                .Select(r => new { r.OrgName, r.RepoName, r.PrimaryLanguage, r.IsPublic })
                .ToListAsync(cancellationToken);

            if (repos.Count == 0) return "(No accessible repositories indexed)";

            var sb = new StringBuilder();
            foreach (var r in repos)
            {
                var lang = !string.IsNullOrEmpty(r.PrimaryLanguage) ? $" ({r.PrimaryLanguage})" : "";
                var vis = r.IsPublic ? "" : " [private]";
                sb.AppendLine($"- {r.OrgName}/{r.RepoName}{lang}{vis}");
            }
            return sb.ToString();
        }
        catch
        {
            return "(Failed to load repository list)";
        }
    }

    /// <summary>
    /// Gets the set of repository IDs the user can access via department assignments.
    /// Returns null if no user is mapped (public-only mode).
    /// </summary>
    private async Task<HashSet<string>?> GetAccessibleRepoIdsAsync(
        IContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_deepWikiUserId))
            return null;

        return await GetAccessibleRepoIdsStaticAsync(context, _deepWikiUserId, cancellationToken);
    }

    /// <summary>
    /// Gets repo IDs accessible to a user via department assignments.
    /// </summary>
    private static async Task<HashSet<string>> GetAccessibleRepoIdsStaticAsync(
        IContext context,
        string userId,
        CancellationToken cancellationToken)
    {
        // Get user's department IDs
        var departmentIds = await context.UserDepartments
            .Where(ud => ud.UserId == userId && !ud.IsDeleted)
            .Select(ud => ud.DepartmentId)
            .ToListAsync(cancellationToken);

        if (departmentIds.Count == 0)
            return new HashSet<string>();

        // Get repository IDs assigned to those departments
        var repoIds = await context.RepositoryAssignments
            .Where(ra => departmentIds.Contains(ra.DepartmentId) && !ra.IsDeleted)
            .Select(ra => ra.RepositoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new HashSet<string>(repoIds);
    }

    /// <summary>
    /// Checks whether the current user can access a specific repository.
    /// Public repos are always accessible. Private repos require department assignment.
    /// </summary>
    private async Task<bool> CanAccessRepositoryAsync(
        IContext context,
        string repositoryId,
        bool isPublic,
        CancellationToken cancellationToken)
    {
        // Public repos are always accessible
        if (isPublic) return true;

        // If no user is mapped, deny access to private repos
        if (string.IsNullOrEmpty(_deepWikiUserId)) return false;

        // Check department assignment
        var accessibleRepoIds = await GetAccessibleRepoIdsAsync(context, cancellationToken);
        return accessibleRepoIds?.Contains(repositoryId) ?? false;
    }
}
