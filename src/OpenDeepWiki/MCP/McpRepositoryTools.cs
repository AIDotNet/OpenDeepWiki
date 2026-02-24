using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using OpenDeepWiki.Agents;
using OpenDeepWiki.Agents.Tools;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;

namespace OpenDeepWiki.MCP;

/// <summary>
/// MCP tools that expose repository documentation to AI clients (Claude, Cursor, etc.).
/// Repository scope is resolved from /api/mcp/{owner}/{repo} via ConfigureSessionOptions.
/// </summary>
[McpServerToolType]
public class McpRepositoryTools
{
    [McpServerTool, Description("Search documentation within the current GitHub repository and return summarized insights.")]
    public static async Task<string> SearchDoc(
        IContext context,
        AgentFactory agentFactory,
        McpServer mcpServer,
        IOptions<RepositoryAnalyzerOptions> repoOptions,
        [Description("Search query or question to answer.")] string query,
        [Description("Maximum number of documents to return (default: 5, max: 20)")] int maxResults = 5,
        [Description("Language code (default: en)")] string language = "en",
        CancellationToken cancellationToken = default)
    {
        var repositoryScopeError = ValidateAndResolveRepositoryScope(mcpServer, out var resolvedOwner, out var resolvedName);
        if (repositoryScopeError != null)
            return JsonSerializer.Serialize(new { error = true, message = repositoryScopeError });

        if (string.IsNullOrWhiteSpace(query))
            return JsonSerializer.Serialize(new { error = true, message = "Search query is required" });

        if (maxResults <= 0) maxResults = 5;
        if (maxResults > 20) maxResults = 20;

        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length == 0)
            return JsonSerializer.Serialize(new { error = true, message = "Search query is required" });

        query = normalizedQuery;
        var loweredQuery = normalizedQuery.ToLowerInvariant();

        var repository = await context.Repositories
            .FirstOrDefaultAsync(r => r.OrgName == resolvedOwner && r.RepoName == resolvedName && !r.IsDeleted, cancellationToken);

        if (repository == null)
            return JsonSerializer.Serialize(new { error = true, message = $"Repository {resolvedOwner}/{resolvedName} not found" });

        var branch = await context.RepositoryBranches
            .FirstOrDefaultAsync(b => b.RepositoryId == repository.Id && !b.IsDeleted, cancellationToken);

        if (branch == null)
            return JsonSerializer.Serialize(new { error = true, message = "No branch found for this repository" });

        var branchLanguage = await context.BranchLanguages
            .FirstOrDefaultAsync(bl => bl.RepositoryBranchId == branch.Id &&
                                       bl.LanguageCode == language && !bl.IsDeleted, cancellationToken);

        if (branchLanguage == null)
            return JsonSerializer.Serialize(new { error = true, message = $"No documentation in language '{language}'" });

        var tools = new List<AITool>();
        var repoPath = BuildRepositoryPath(repoOptions.Value, resolvedOwner!, resolvedName!);
        if (Directory.Exists(repoPath))
        {
            try
            {
                var gitTool = new GitTool(repoPath);
                tools.AddRange(gitTool.GetTools());
            }
            catch
            {
                // Ignore GitTool init failures; doc search still works without code tools.
            }
        }

        var matchingDocs = await context.DocCatalogs
            .Where(c => c.BranchLanguageId == branchLanguage.Id &&
                        !c.IsDeleted && !string.IsNullOrEmpty(c.DocFileId))
            .Join(context.DocFiles.Where(d => !d.IsDeleted),
                  c => c.DocFileId, d => d.Id,
                  (c, d) => new { Catalog = c, DocFile = d })
            .Where(x => (!string.IsNullOrEmpty(x.DocFile.Content) &&
                         x.DocFile.Content.ToLower().Contains(loweredQuery))
                     || (!string.IsNullOrEmpty(x.Catalog.Title) &&
                         x.Catalog.Title.ToLower().Contains(loweredQuery)))
            .Select(x => new
            {
                x.Catalog.Title,
                x.Catalog.Path,
                x.DocFile.Content
            })
            .Take(maxResults)
            .ToListAsync(cancellationToken);

        var matches = matchingDocs.Select(doc =>
        {
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

            return new DocSearchMatch
            {
                Title = doc.Title,
                Path = doc.Path,
                MatchLine = matchLine,
                Snippet = snippet.Length > 500 ? snippet[..500] + "..." : snippet
            };
        }).ToList();

        var summary = await BuildSearchSummaryAsync(
            context,
            agentFactory,
            resolvedOwner!,
            resolvedName!,
            query,
            matches,
            tools,
            cancellationToken);

        var results = matches.Select(m => new
        {
            title = m.Title,
            path = m.Path,
            matchLine = m.MatchLine,
            snippet = m.Snippet
        });

        return JsonSerializer.Serialize(new
        {
            repository = $"{resolvedOwner}/{resolvedName}",
            branch = branch.BranchName,
            language,
            query,
            matchCount = matches.Count,
            results,
            summary
        });
    }

    [McpServerTool, Description("Get the repository directory structure. Useful for understanding module layout.")]
    public static async Task<string> GetRepoStructure(
        IContext context,
        McpServer mcpServer,
        IOptions<RepositoryAnalyzerOptions> repoOptions,
        [Description("Optional subdirectory relative to repo root, default is repository root.")] string? path = null,
        [Description("Maximum depth to traverse (default: 3)")] int maxDepth = 3,
        [Description("Maximum entries to return (default: 200)")] int maxEntries = 200,
        CancellationToken cancellationToken = default)
    {
        var repositoryScopeError = ValidateAndResolveRepositoryScope(mcpServer, out var resolvedOwner, out var resolvedName);
        if (repositoryScopeError != null)
            return JsonSerializer.Serialize(new { error = true, message = repositoryScopeError });

        if (maxDepth <= 0) maxDepth = 1;
        if (maxEntries <= 0) maxEntries = 200;

        var repository = await context.Repositories
            .FirstOrDefaultAsync(r => r.OrgName == resolvedOwner && r.RepoName == resolvedName && !r.IsDeleted, cancellationToken);

        if (repository == null)
            return JsonSerializer.Serialize(new { error = true, message = $"Repository {resolvedOwner}/{resolvedName} not found" });

        var repoPath = BuildRepositoryPath(repoOptions.Value, resolvedOwner!, resolvedName!);
        if (!Directory.Exists(repoPath))
            return JsonSerializer.Serialize(new { error = true, message = "Repository workspace not found on server" });

        var normalizedPath = NormalizeRelativePath(path);
        var targetPath = string.IsNullOrEmpty(normalizedPath)
            ? repoPath
            : Path.Combine(repoPath, normalizedPath);

        if (!targetPath.StartsWith(repoPath, StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { error = true, message = "Invalid path" });

        if (!Directory.Exists(targetPath))
            return JsonSerializer.Serialize(new { error = true, message = $"Path '{normalizedPath}' does not exist" });

        var entries = await Task.Run(() => BuildDirectoryTree(targetPath, maxDepth, maxEntries), cancellationToken);
        var truncated = entries.Count >= maxEntries;

        return JsonSerializer.Serialize(new
        {
            repository = $"{resolvedOwner}/{resolvedName}",
            root = string.IsNullOrEmpty(normalizedPath) ? "/" : normalizedPath,
            depth = maxDepth,
            entryCount = entries.Count,
            truncated,
            entries
        });
    }

    [McpServerTool, Description("Read a file from the current repository. Returns file content with line numbers.")]
    public static async Task<string> ReadFile(
        IContext context,
        McpServer mcpServer,
        IOptions<RepositoryAnalyzerOptions> repoOptions,
        [Description("Relative file path from repository root")] string path,
        [Description("Line number to start reading from (1-based). Default: 1")] int offset = 1,
        [Description("Maximum number of lines to read. Default: 2000")] int limit = 2000,
        CancellationToken cancellationToken = default)
    {
        var repositoryScopeError = ValidateAndResolveRepositoryScope(mcpServer, out var resolvedOwner, out var resolvedName);
        if (repositoryScopeError != null)
            return JsonSerializer.Serialize(new { error = true, message = repositoryScopeError });

        if (string.IsNullOrWhiteSpace(path))
            return JsonSerializer.Serialize(new { error = true, message = "File path is required" });

        var repository = await context.Repositories
            .FirstOrDefaultAsync(r => r.OrgName == resolvedOwner && r.RepoName == resolvedName && !r.IsDeleted, cancellationToken);

        if (repository == null)
            return JsonSerializer.Serialize(new { error = true, message = $"Repository {resolvedOwner}/{resolvedName} not found" });

        var repoPath = BuildRepositoryPath(repoOptions.Value, resolvedOwner!, resolvedName!);
        if (!Directory.Exists(repoPath))
            return JsonSerializer.Serialize(new { error = true, message = "Repository workspace not found on server" });

        var gitTool = new GitTool(repoPath);
        var content = await gitTool.ReadAsync(path, offset, limit, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            repository = $"{resolvedOwner}/{resolvedName}",
            path,
            content
        });
    }

    private static string? ValidateAndResolveRepositoryScope(
        McpServer mcpServer,
        out string? resolvedOwner,
        out string? resolvedName)
    {
        var scope = McpRepositoryScopeAccessor.GetScope(mcpServer);
        resolvedOwner = scope.Owner;
        resolvedName = scope.Repo;

        if (string.IsNullOrWhiteSpace(resolvedOwner) || string.IsNullOrWhiteSpace(resolvedName))
        {
            return "Repository scope is required. Call MCP via /api/mcp/{owner}/{repo}.";
        }

        return null;
    }

    private static async Task<string?> BuildSearchSummaryAsync(
        IContext context,
        AgentFactory agentFactory,
        string owner,
        string repo,
        string query,
        IReadOnlyList<DocSearchMatch> matches,
        IReadOnlyList<AITool> tools,
        CancellationToken cancellationToken)
    {
        if (matches.Count == 0)
            return "未找到匹配的文档内容。";

        var modelConfig = await ResolveMcpModelConfigAsync(context, cancellationToken);
        if (modelConfig == null)
            return null;

        var requestOptions = new AiRequestOptions
        {
            ApiKey = modelConfig.ApiKey,
            Endpoint = modelConfig.Endpoint,
            RequestType = ParseRequestType(modelConfig.Provider)
        };

        var agentOptions = new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 12000,
                ToolMode = ChatToolMode.Auto
            }
        };

        var (agent, _) = agentFactory.CreateChatClientWithTools(
            modelConfig.ModelId,
            tools.Count == 0 ? Array.Empty<AITool>() : tools.ToArray(),
            agentOptions,
            requestOptions);

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine($"Repository: {owner}/{repo}");
        promptBuilder.AppendLine($"User Question: {query}");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Search Results:");
        foreach (var match in matches)
        {
            promptBuilder.AppendLine($"- {match.Title} ({match.Path})");
            if (!string.IsNullOrWhiteSpace(match.Snippet))
            {
                promptBuilder.AppendLine($"  Snippet: {match.Snippet}");
            }
        }
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("=== INSTRUCTIONS ===");
        promptBuilder.AppendLine("You are an expert repository documentation assistant. Analyze the search results above and provide a comprehensive, well-structured answer to the user's question.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Your response MUST include the following sections:");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("1. **Executive Summary** (2-3 sentences)");
        promptBuilder.AppendLine("   - Provide a concise, direct answer to the user's question.");
        promptBuilder.AppendLine("   - Highlight the most critical information or conclusion.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("2. **Detailed Explanation**");
        promptBuilder.AppendLine("   - Break down key concepts, configurations, or implementation steps in separate paragraphs.");
        promptBuilder.AppendLine("   - Reference specific document paths from the search results when applicable (e.g., 'As documented in [path]...').");
        promptBuilder.AppendLine("   - Include code examples, configuration snippets, or command-line instructions if relevant.");
        promptBuilder.AppendLine("   - Explain the rationale behind recommendations or design decisions.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("3. **Recommended Next Steps**");
        promptBuilder.AppendLine("   - Suggest specific documents or sections the user should read for deeper understanding.");
        promptBuilder.AppendLine("   - Provide actionable follow-up tasks or verification steps.");
        promptBuilder.AppendLine("   - If information is incomplete, clearly state what's missing and suggest troubleshooting approaches.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("=== GUIDELINES ===");
        promptBuilder.AppendLine("- Use clear, professional technical writing suitable for developers.");
        promptBuilder.AppendLine("- Maintain consistency with the project's terminology and conventions.");
        promptBuilder.AppendLine("- Be thorough but avoid unnecessary verbosity.");
        promptBuilder.AppendLine("- If the search results don't fully answer the question, acknowledge the gaps and explain what additional context would help.");
        promptBuilder.AppendLine("- When referencing code or configuration, use proper formatting (code blocks for multi-line, inline for single-line).");
        promptBuilder.AppendLine("- If multiple search results are relevant, synthesize information from all of them rather than treating them separately.");
        promptBuilder.AppendLine("- Consider the language context: if documentation is in a specific language, maintain consistency in terminology.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("=== RESPONSE FORMAT ===");
        promptBuilder.AppendLine("Provide your answer in the following format:");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("## Executive Summary");
        promptBuilder.AppendLine("[Your concise summary here]");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("## Detailed Explanation");
        promptBuilder.AppendLine("[Your detailed explanation here]");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("## Recommended Next Steps");
        promptBuilder.AppendLine("[Your recommendations here]");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are an expert repository documentation assistant for developers. Your role is to provide clear, comprehensive, and actionable answers based on repository documentation search results. Prioritize accuracy, completeness, and practical guidance. Structure your responses professionally and cite relevant documentation paths when applicable."),
            new(ChatRole.User, promptBuilder.ToString())
        };

        var thread = await agent.CreateSessionAsync(cancellationToken);
        var summaryBuilder = new StringBuilder();
        await foreach (var update in agent.RunStreamingAsync(messages, thread, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                summaryBuilder.Append(update.Text);
            }
        }

        return summaryBuilder.ToString().Trim();
    }

    private static async Task<ModelConfig?> ResolveMcpModelConfigAsync(
        IContext context,
        CancellationToken cancellationToken)
    {
        var providerModelId = await context.McpProviders
            .Where(p => p.IsActive && !p.IsDeleted && !string.IsNullOrEmpty(p.ModelConfigId))
            .OrderBy(p => p.SortOrder)
            .Select(p => p.ModelConfigId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrEmpty(providerModelId))
        {
            var providerModel = await context.ModelConfigs
                .FirstOrDefaultAsync(m => m.Id == providerModelId && m.IsActive && !m.IsDeleted, cancellationToken);
            if (providerModel != null) return providerModel;
        }

        return await context.ModelConfigs
            .Where(m => m.IsActive && !m.IsDeleted)
            .OrderByDescending(m => m.IsDefault)
            .ThenByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static AiRequestType ParseRequestType(string? provider)
    {
        return provider?.ToLowerInvariant() switch
        {
            "openai" => AiRequestType.OpenAI,
            "openairesponses" => AiRequestType.OpenAIResponses,
            "anthropic" => AiRequestType.Anthropic,
            "azureopenai" => AiRequestType.AzureOpenAI,
            _ => AiRequestType.OpenAI
        };
    }

    private static string BuildRepositoryPath(RepositoryAnalyzerOptions options, string owner, string repo)
    {
        var safeOwner = SanitizePathComponent(owner);
        var safeRepo = SanitizePathComponent(repo);
        return Path.Combine(options.RepositoriesDirectory, safeOwner, safeRepo, "tree");
    }

    private static string SanitizePathComponent(string component)
    {
        var sanitized = component
            .Replace('/', '_')
            .Replace('\\', '_')
            .Replace("..", "_")
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "_" : sanitized;
    }

    private static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var normalized = path.Replace('\\', '/').Trim('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != "." && p != "..");
        return string.Join('/', parts);
    }

    private static List<string> BuildDirectoryTree(string rootPath, int maxDepth, int maxEntries)
    {
        var entries = new List<string>();
        TraverseDirectory(rootPath, 0, maxDepth, maxEntries, entries, string.Empty);
        return entries;
    }

    private static void TraverseDirectory(
        string currentPath,
        int depth,
        int maxDepth,
        int maxEntries,
        List<string> entries,
        string indent)
    {
        if (entries.Count >= maxEntries) return;

        var directories = Directory.EnumerateDirectories(currentPath)
            .Where(d => !IsHiddenEntry(d))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var dir in directories)
        {
            if (entries.Count >= maxEntries) return;
            var name = Path.GetFileName(dir) + "/";
            entries.Add($"{indent}{name}");
            if (depth + 1 < maxDepth)
            {
                TraverseDirectory(dir, depth + 1, maxDepth, maxEntries, entries, indent + "  ");
            }
        }

        var files = Directory.EnumerateFiles(currentPath)
            .Where(f => !IsHiddenEntry(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files)
        {
            if (entries.Count >= maxEntries) return;
            var name = Path.GetFileName(file);
            entries.Add($"{indent}{name}");
        }
    }

    private static bool IsHiddenEntry(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith(".", StringComparison.Ordinal) ||
               string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DocSearchMatch
    {
        public string Title { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public int MatchLine { get; init; }
        public string Snippet { get; init; } = string.Empty;
    }
}
