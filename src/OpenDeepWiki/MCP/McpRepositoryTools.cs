using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using OpenDeepWiki.EFCore;

namespace OpenDeepWiki.MCP;

/// <summary>
/// MCP tools that expose repository documentation to AI clients (Claude, Cursor, etc.).
/// All tools verify user access through department assignments before returning data.
/// </summary>
[McpServerToolType]
public class McpRepositoryTools
{
    [McpServerTool, Description("List all repositories you have access to. Returns repository names, owners, and status.")]
    public static async Task<string> ListRepositories(
        IHttpContextAccessor httpContextAccessor,
        IMcpUserResolver userResolver)
    {
        var user = await ResolveUserOrThrow(httpContextAccessor, userResolver);
        var repos = await userResolver.GetAccessibleRepositoriesAsync(user.UserId);

        if (repos.Count == 0)
            return JsonSerializer.Serialize(new { message = "No repositories available. You may not be assigned to any department." });

        return JsonSerializer.Serialize(new
        {
            count = repos.Count,
            repositories = repos.Select(r => new
            {
                owner = r.Owner,
                name = r.Name,
                status = r.Status,
                department = r.Department
            })
        });
    }

    [McpServerTool, Description("Get the document catalog (table of contents) for a repository. Use this to discover available documentation paths before reading documents.")]
    public static async Task<string> GetDocumentCatalog(
        IHttpContextAccessor httpContextAccessor,
        IMcpUserResolver userResolver,
        IContext context,
        [Description("Repository owner/organization name")] string owner,
        [Description("Repository name")] string name,
        [Description("Language code (default: en)")] string language = "en")
    {
        var user = await ResolveUserOrThrow(httpContextAccessor, userResolver);

        if (!await userResolver.CanAccessRepositoryAsync(user.UserId, owner, name))
            return JsonSerializer.Serialize(new { error = true, message = $"Access denied to {owner}/{name}" });

        var repository = await context.Repositories
            .FirstOrDefaultAsync(r => r.OrgName == owner && r.RepoName == name && !r.IsDeleted);

        if (repository == null)
            return JsonSerializer.Serialize(new { error = true, message = $"Repository {owner}/{name} not found" });

        // Get default branch
        var branch = await context.RepositoryBranches
            .FirstOrDefaultAsync(b => b.RepositoryId == repository.Id && !b.IsDeleted);

        if (branch == null)
            return JsonSerializer.Serialize(new { error = true, message = "No branch found for this repository" });

        var branchLanguage = await context.BranchLanguages
            .FirstOrDefaultAsync(bl => bl.RepositoryBranchId == branch.Id &&
                                       bl.LanguageCode == language && !bl.IsDeleted);

        if (branchLanguage == null)
            return JsonSerializer.Serialize(new { error = true, message = $"No documentation in language '{language}'" });

        var catalogs = await context.DocCatalogs
            .Where(c => c.BranchLanguageId == branchLanguage.Id &&
                        !c.IsDeleted && !string.IsNullOrEmpty(c.DocFileId))
            .OrderBy(c => c.Order)
            .Select(c => new { c.Title, c.Path, c.Order, c.ParentId })
            .ToListAsync();

        if (catalogs.Count == 0)
            return JsonSerializer.Serialize(new { error = true, message = "No documents available for this repository" });

        return JsonSerializer.Serialize(new
        {
            repository = $"{owner}/{name}",
            branch = branch.BranchName,
            language,
            documentCount = catalogs.Count,
            documents = catalogs.Select(c => new
            {
                title = c.Title,
                path = c.Path,
                order = c.Order,
                hasParent = c.ParentId != null
            })
        });
    }

    [McpServerTool, Description("Read a specific document from repository documentation. Use GetDocumentCatalog first to find available paths.")]
    public static async Task<string> ReadDocument(
        IHttpContextAccessor httpContextAccessor,
        IMcpUserResolver userResolver,
        IContext context,
        [Description("Repository owner/organization name")] string owner,
        [Description("Repository name")] string name,
        [Description("Document path from the catalog")] string path,
        [Description("Starting line number (1-based, inclusive). Default: 1")] int startLine = 1,
        [Description("Ending line number (1-based, inclusive). Max 200 lines per request. Default: 200")] int endLine = 200,
        [Description("Language code (default: en)")] string language = "en")
    {
        var user = await ResolveUserOrThrow(httpContextAccessor, userResolver);

        if (!await userResolver.CanAccessRepositoryAsync(user.UserId, owner, name))
            return JsonSerializer.Serialize(new { error = true, message = $"Access denied to {owner}/{name}" });

        if (startLine < 1) startLine = 1;
        if (endLine < startLine) endLine = startLine;
        if (endLine - startLine > 200) endLine = startLine + 200;

        var repository = await context.Repositories
            .FirstOrDefaultAsync(r => r.OrgName == owner && r.RepoName == name && !r.IsDeleted);

        if (repository == null)
            return JsonSerializer.Serialize(new { error = true, message = $"Repository {owner}/{name} not found" });

        var branch = await context.RepositoryBranches
            .FirstOrDefaultAsync(b => b.RepositoryId == repository.Id && !b.IsDeleted);

        if (branch == null)
            return JsonSerializer.Serialize(new { error = true, message = "No branch found" });

        var branchLanguage = await context.BranchLanguages
            .FirstOrDefaultAsync(bl => bl.RepositoryBranchId == branch.Id &&
                                       bl.LanguageCode == language && !bl.IsDeleted);

        if (branchLanguage == null)
            return JsonSerializer.Serialize(new { error = true, message = $"No documentation in language '{language}'" });

        var catalog = await context.DocCatalogs
            .FirstOrDefaultAsync(c => c.BranchLanguageId == branchLanguage.Id &&
                                      c.Path == path && !c.IsDeleted);

        if (catalog == null)
            return JsonSerializer.Serialize(new { error = true, message = $"Document '{path}' not found" });

        if (string.IsNullOrEmpty(catalog.DocFileId))
            return JsonSerializer.Serialize(new { error = true, message = $"Document '{path}' has no content" });

        var docFile = await context.DocFiles
            .FirstOrDefaultAsync(d => d.Id == catalog.DocFileId && !d.IsDeleted);

        if (docFile == null)
            return JsonSerializer.Serialize(new { error = true, message = $"Document content not found" });

        var allLines = docFile.Content.Split('\n');
        var totalLines = allLines.Length;
        var actualEndLine = Math.Min(endLine, totalLines);

        if (startLine > totalLines)
            return JsonSerializer.Serialize(new { error = true, message = $"startLine ({startLine}) exceeds total lines ({totalLines})" });

        var selectedLines = allLines.Skip(startLine - 1).Take(actualEndLine - startLine + 1);
        var content = string.Join("\n", selectedLines);

        List<string>? sourceFiles = null;
        if (!string.IsNullOrEmpty(docFile.SourceFiles))
        {
            try { sourceFiles = JsonSerializer.Deserialize<List<string>>(docFile.SourceFiles); }
            catch { /* ignore parse failure */ }
        }

        return JsonSerializer.Serialize(new
        {
            repository = $"{owner}/{name}",
            path,
            title = catalog.Title,
            content,
            startLine,
            endLine = actualEndLine,
            totalLines,
            sourceFiles
        });
    }

    [McpServerTool, Description("Search across all documents in a repository for content matching a query. Returns matching document paths and snippets.")]
    public static async Task<string> SearchDocuments(
        IHttpContextAccessor httpContextAccessor,
        IMcpUserResolver userResolver,
        IContext context,
        [Description("Repository owner/organization name")] string owner,
        [Description("Repository name")] string name,
        [Description("Search query (case-insensitive text search)")] string query,
        [Description("Language code (default: en)")] string language = "en")
    {
        var user = await ResolveUserOrThrow(httpContextAccessor, userResolver);

        if (!await userResolver.CanAccessRepositoryAsync(user.UserId, owner, name))
            return JsonSerializer.Serialize(new { error = true, message = $"Access denied to {owner}/{name}" });

        if (string.IsNullOrWhiteSpace(query))
            return JsonSerializer.Serialize(new { error = true, message = "Search query is required" });

        var repository = await context.Repositories
            .FirstOrDefaultAsync(r => r.OrgName == owner && r.RepoName == name && !r.IsDeleted);

        if (repository == null)
            return JsonSerializer.Serialize(new { error = true, message = $"Repository {owner}/{name} not found" });

        var branch = await context.RepositoryBranches
            .FirstOrDefaultAsync(b => b.RepositoryId == repository.Id && !b.IsDeleted);

        if (branch == null)
            return JsonSerializer.Serialize(new { error = true, message = "No branch found" });

        var branchLanguage = await context.BranchLanguages
            .FirstOrDefaultAsync(bl => bl.RepositoryBranchId == branch.Id &&
                                       bl.LanguageCode == language && !bl.IsDeleted);

        if (branchLanguage == null)
            return JsonSerializer.Serialize(new { error = true, message = $"No documentation in language '{language}'" });

        // Search across all doc files for this branch/language
        var matchingDocs = await context.DocCatalogs
            .Where(c => c.BranchLanguageId == branchLanguage.Id &&
                        !c.IsDeleted && !string.IsNullOrEmpty(c.DocFileId))
            .Join(context.DocFiles.Where(d => !d.IsDeleted),
                  c => c.DocFileId, d => d.Id,
                  (c, d) => new { Catalog = c, DocFile = d })
            .Where(x => x.DocFile.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || x.Catalog.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(x => new
            {
                x.Catalog.Title,
                x.Catalog.Path,
                x.DocFile.Content
            })
            .Take(10)
            .ToListAsync();

        var results = matchingDocs.Select(doc =>
        {
            // Find first matching line for snippet
            var lines = doc.Content.Split('\n');
            var matchLine = -1;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matchLine = i + 1;
                    break;
                }
            }

            var snippetStart = Math.Max(0, (matchLine > 0 ? matchLine - 1 : 0) - 2);
            var snippet = string.Join("\n", lines.Skip(snippetStart).Take(5));

            return new
            {
                title = doc.Title,
                path = doc.Path,
                matchLine,
                snippet = snippet.Length > 500 ? snippet[..500] + "..." : snippet
            };
        });

        return JsonSerializer.Serialize(new
        {
            repository = $"{owner}/{name}",
            query,
            matchCount = matchingDocs.Count,
            results
        });
    }

    private static async Task<McpUserInfo> ResolveUserOrThrow(
        IHttpContextAccessor httpContextAccessor, IMcpUserResolver userResolver)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal == null)
            throw new UnauthorizedAccessException("No authentication context available");

        var user = await userResolver.ResolveUserAsync(principal);
        if (user == null)
            throw new UnauthorizedAccessException("User not found in DeepWiki. Please ensure your Google account is registered.");

        return user;
    }
}
