using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Anthropic.Models.Messages;
using AIDotNet.Toon;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI.Responses;
using OpenDeepWiki.Agents;
using OpenDeepWiki.Agents.Tools;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Chat;
using OpenDeepWiki.Services.Prompts;
using OpenDeepWiki.Services.Repositories;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace OpenDeepWiki.Services.Wiki;

/// <summary>
/// Token usage statistics for tracking AI model consumption.
/// </summary>
public class TokenUsageStats
{
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public int TotalTokens => InputTokens + OutputTokens;
    public int ToolCallCount { get; private set; }

    public void Add(int inputTokens, int outputTokens, int toolCalls = 0)
    {
        InputTokens += inputTokens;
        OutputTokens += outputTokens;
        ToolCallCount += toolCalls;
    }

    public void Reset()
    {
        InputTokens = 0;
        OutputTokens = 0;
        ToolCallCount = 0;
    }

    public override string ToString()
    {
        return $"InputTokens: {InputTokens}, OutputTokens: {OutputTokens}, TotalTokens: {TotalTokens}, ToolCalls: {ToolCallCount}";
    }
}

/// <summary>
/// Implementation of IWikiGenerator using AI agents.
/// Generates wiki catalog structures and document content using configured AI models.
/// </summary>
public class WikiGenerator : IWikiGenerator
{
    // Compiled regex for better performance when removing <think> tags
    private static readonly Regex ThinkTagRegex = new(
        @"<think>[\s\S]*?</think>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private readonly AgentFactory _agentFactory;
    private readonly IPromptPlugin _promptPlugin;
    private readonly WikiGeneratorOptions _options;
    private readonly IContext _context;
    private readonly IContextFactory _contextFactory;
    private readonly ILogger<WikiGenerator> _logger;
    private readonly IProcessingLogService _processingLogService;
    private readonly ISkillToolConverter _skillToolConverter;

    // Use AsyncLocal for thread-safe repository ID tracking in concurrent scenarios
    private static readonly AsyncLocal<string?> _currentRepositoryId = new();

    /// <summary>
    /// Initializes a new instance of WikiGenerator.
    /// </summary>
    public WikiGenerator(
        AgentFactory agentFactory,
        IPromptPlugin promptPlugin,
        IOptions<WikiGeneratorOptions> options,
        IContext context,
        IContextFactory contextFactory,
        ILogger<WikiGenerator> logger,
        IProcessingLogService processingLogService,
        ISkillToolConverter skillToolConverter)
    {
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _promptPlugin = promptPlugin ?? throw new ArgumentNullException(nameof(promptPlugin));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processingLogService = processingLogService ?? throw new ArgumentNullException(nameof(processingLogService));
        _skillToolConverter = skillToolConverter ?? throw new ArgumentNullException(nameof(skillToolConverter));

        _logger.LogDebug(
            "WikiGenerator initialized. CatalogModel: {CatalogModel}, ContentModel: {ContentModel}, MaxRetryAttempts: {MaxRetry}",
            _options.CatalogModel, _options.ContentModel, _options.MaxRetryAttempts);
    }

    /// <summary>
    /// 设置当前处理的仓库ID
    /// </summary>
    public void SetCurrentRepository(string repositoryId)
    {
        _currentRepositoryId.Value = repositoryId;
    }

    /// <inheritdoc />
    public async Task GenerateMindMapAsync(
        RepositoryWorkspace workspace,
        BranchLanguage branchLanguage,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Starting mind map generation. Repository: {Org}/{Repo}, Branch: {Branch}, Language: {Language}, BranchLanguageId: {BranchLanguageId}",
            workspace.Organization, workspace.RepositoryName,
            workspace.BranchName, branchLanguage.LanguageCode, branchLanguage.Id);

        // 更新状态为处理中
        branchLanguage.MindMapStatus = MindMapStatus.Processing;
        await _context.SaveChangesAsync(cancellationToken);

        await LogProcessingAsync(ProcessingStep.Catalog, $"Starting mind map generation ({branchLanguage.LanguageCode})", cancellationToken);

        try
        {
            // 收集仓库上下文
            _logger.LogDebug("Pre-collecting repository context for mind map");
            var repoContext = await CollectRepositoryContextAsync(workspace.WorkingDirectory, cancellationToken);
            _logger.LogDebug("Repository context collected. ProjectType: {ProjectType}, EntryPoints: {EntryPoints}",
                repoContext.ProjectType, string.Join(", ", repoContext.EntryPoints));

            _logger.LogDebug("Loading mindmap-generator prompt template");
            var prompt = await _promptPlugin.LoadPromptAsync(
                "mindmap-generator",
                new Dictionary<string, string>
                {
                    ["repository_name"] = $"{workspace.Organization}/{workspace.RepositoryName}",
                    ["language"] = branchLanguage.LanguageCode,
                    ["project_type"] = repoContext.ProjectType,
                    ["file_tree"] = repoContext.DirectoryTree,
                    ["readme_content"] = repoContext.ReadmeContent,
                    ["key_files"] = string.Join(", ", repoContext.KeyFiles),
                    ["entry_points"] = string.Join("\n", repoContext.EntryPoints.Select(e => $"- {e}"))
                },
                cancellationToken);
            _logger.LogDebug("Prompt template loaded. Length: {PromptLength} chars", prompt.Length);

            _logger.LogDebug("Initializing tools for mind map generation");
            var gitTool = new GitTool(workspace.WorkingDirectory);
            var mindMapTool = new MindMapTool(_context, branchLanguage.Id);
            var tools = await BuildToolsAsync(
                gitTool.GetTools().Concat(mindMapTool.GetTools()),
                cancellationToken);
            _logger.LogDebug("Tools initialized. ToolCount: {ToolCount}, Tools: {ToolNames}",
                tools.Length, string.Join(", ", tools.Select(t => t.Name)));

            var userMessage = $@"Generate project architecture mind map for: {workspace.Organization}/{workspace.RepositoryName}

Project Type: {repoContext.ProjectType}
Entry Points: {string.Join(", ", repoContext.EntryPoints.Take(5))}

Execute the workflow now. Read entry point files to understand the architecture, then generate a comprehensive mind map in {branchLanguage.LanguageCode}.
Remember to call WriteMindMapAsync with the complete mind map content.";

            await ExecuteAgentWithRetryAsync(
                _options.CatalogModel,
                _options.GetCatalogRequestOptions(),
                prompt,
                userMessage,
                tools,
                "MindMapGeneration",
                ProcessingStep.Catalog,
                cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "Mind map generation completed successfully. Repository: {Org}/{Repo}, Language: {Language}, Duration: {Duration}ms",
                workspace.Organization, workspace.RepositoryName, branchLanguage.LanguageCode, stopwatch.ElapsedMilliseconds);

            await LogProcessingAsync(ProcessingStep.Catalog,
                $"Mind map generation complete, time: {stopwatch.ElapsedMilliseconds}ms",
                cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Mind map generation failed. Repository: {Org}/{Repo}, Language: {Language}, Duration: {Duration}ms",
                workspace.Organization, workspace.RepositoryName, branchLanguage.LanguageCode, stopwatch.ElapsedMilliseconds);

            // 更新状态为failed
            branchLanguage.MindMapStatus = MindMapStatus.Failed;
            await _context.SaveChangesAsync(cancellationToken);

            throw;
        }
    }

    /// <inheritdoc />
    public async Task GenerateCatalogAsync(
        RepositoryWorkspace workspace,
        BranchLanguage branchLanguage,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Starting catalog generation. Repository: {Org}/{Repo}, Branch: {Branch}, Language: {Language}, BranchLanguageId: {BranchLanguageId}",
            workspace.Organization, workspace.RepositoryName,
            workspace.BranchName, branchLanguage.LanguageCode, branchLanguage.Id);

        // 记录开始生成目录
        await LogProcessingAsync(ProcessingStep.Catalog, $"Starting catalog generation ({branchLanguage.LanguageCode})", cancellationToken);

        try
        {
            // 收集仓库上下文（目录结构、项目类型、README等）
            _logger.LogDebug("Pre-collecting repository context");
            var repoContext = await CollectRepositoryContextAsync(workspace.WorkingDirectory, cancellationToken);
            _logger.LogDebug("Repository context collected. ProjectType: {ProjectType}, EntryPoints: {EntryPoints}", 
                repoContext.ProjectType, string.Join(", ", repoContext.EntryPoints));

            _logger.LogDebug("Loading catalog-generator prompt template");
            var prompt = await _promptPlugin.LoadPromptAsync(
                "catalog-generator",
                new Dictionary<string, string>
                {
                    ["repository_name"] = $"{workspace.Organization}/{workspace.RepositoryName}",
                    ["language"] = branchLanguage.LanguageCode,
                    ["project_type"] = repoContext.ProjectType,
                    ["file_tree"] = repoContext.DirectoryTree,
                    ["readme_content"] = repoContext.ReadmeContent,
                    ["key_files"] = string.Join(", ", repoContext.KeyFiles),
                    ["entry_points"] = string.Join("\n", repoContext.EntryPoints.Select(e => $"- {e}"))
                },
                cancellationToken);
            _logger.LogDebug("Prompt template loaded. Length: {PromptLength} chars", prompt.Length);

            _logger.LogDebug("Initializing tools for catalog generation");
            var gitTool = new GitTool(workspace.WorkingDirectory);
            var catalogStorage = new CatalogStorage(_context, branchLanguage.Id);
            var catalogTool = new CatalogTool(catalogStorage);
            var tools = await BuildToolsAsync(
                gitTool.GetTools().Concat(catalogTool.GetTools()),
                cancellationToken);
            _logger.LogDebug("Tools initialized. ToolCount: {ToolCount}, Tools: {ToolNames}",
                tools.Length, string.Join(", ", tools.Select(t => t.Name)));

            var userMessage = $@"Generate Wiki catalog for: {workspace.Organization}/{workspace.RepositoryName}

Project Type: {repoContext.ProjectType}
Entry Points: {string.Join(", ", repoContext.EntryPoints.Take(5))}

Execute the workflow now. Read entry point files to understand the architecture, then generate a comprehensive catalog in {branchLanguage.LanguageCode}.";

            await ExecuteAgentWithRetryAsync(
                _options.CatalogModel,
                _options.GetCatalogRequestOptions(),
                prompt,
                userMessage,
                tools,
                "CatalogGeneration",
                ProcessingStep.Catalog,
                cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "Catalog generation completed successfully. Repository: {Org}/{Repo}, Language: {Language}, Duration: {Duration}ms",
                workspace.Organization, workspace.RepositoryName, branchLanguage.LanguageCode, stopwatch.ElapsedMilliseconds);

            await LogProcessingAsync(ProcessingStep.Catalog, 
                $"Catalog generation complete, time: {stopwatch.ElapsedMilliseconds}ms", 
                cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Catalog generation failed. Repository: {Org}/{Repo}, Language: {Language}, Duration: {Duration}ms",
                workspace.Organization, workspace.RepositoryName, branchLanguage.LanguageCode, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }


    /// <inheritdoc />
    public async Task GenerateDocumentsAsync(
        RepositoryWorkspace workspace,
        BranchLanguage branchLanguage,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Starting document generation. Repository: {Org}/{Repo}, Branch: {Branch}, Language: {Language}",
            workspace.Organization, workspace.RepositoryName,
            workspace.BranchName, branchLanguage.LanguageCode);

        // 记录Generating document
        await LogProcessingAsync(ProcessingStep.Content, $"Starting document content generation ({branchLanguage.LanguageCode})", cancellationToken);

        // Get all catalog items that need content generation
        var catalogStorage = new CatalogStorage(_context, branchLanguage.Id);
        var catalogJson = await catalogStorage.GetCatalogJsonAsync(cancellationToken);
        var catalogItems = GetAllCatalogPaths(catalogJson);

        var parallelCount = _options.ParallelCount;
        _logger.LogInformation(
            "Found {Count} catalog items to generate content for. Repository: {Org}/{Repo}, ParallelCount: {ParallelCount}",
            catalogItems.Count, workspace.Organization, workspace.RepositoryName, parallelCount);

        await LogProcessingAsync(ProcessingStep.Content, $"发现 {catalogItems.Count} documents to generate, parallelism: {parallelCount}", cancellationToken);

        if (catalogItems.Count > 0)
        {
            _logger.LogDebug("Catalog items to process: {Items}",
                string.Join(", ", catalogItems.Select(i => $"{i.Path}:{i.Title}")));
        }

        var successCount = 0;
        var failCount = 0;
        var startedCount = 0;
        var completedCount = 0;

        // Use Parallel.ForEachAsync for better parallel control with timeout protection
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelCount,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(catalogItems, parallelOptions, async (item, ct) =>
        {
            var startedIndex = Interlocked.Increment(ref startedCount);
            var completionStatus = "success";
            var shouldLogCompletion = false;

            try
            {
                await LogProcessingAsync(ProcessingStep.Content, $"Generating document ({startedIndex}/{catalogItems.Count}): {item.Title}", ct);

                // Add timeout protection for each document generation
                var generationTimeout = TimeSpan.FromMinutes(_options.DocumentGenerationTimeoutMinutes);
                using var timeoutCts = new CancellationTokenSource(generationTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                try
                {
                    await GenerateDocumentContentAsync(
                            workspace, branchLanguage, item.Path, item.Title, linkedCts.Token)
                        .WaitAsync(generationTimeout, ct);
                }
                catch (TimeoutException)
                {
                    linkedCts.Cancel();
                    throw;
                }

                Interlocked.Increment(ref successCount);
                shouldLogCompletion = true;

                _logger.LogDebug(
                    "Document generated successfully. Path: {Path}, Title: {Title}, Progress: {Current}/{Total}",
                    item.Path, item.Title, startedIndex, catalogItems.Count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // User cancelled the operation
                Interlocked.Increment(ref failCount);
                _logger.LogWarning(
                    "Document generation cancelled by user. Path: {Path}, Title: {Title}",
                    item.Path, item.Title);
                throw; // Re-throw to stop the parallel loop
            }
            catch (TimeoutException)
            {
                // Hard timeout occurred (SDK may ignore cancellation)
                Interlocked.Increment(ref failCount);
                completionStatus = "timeout";
                shouldLogCompletion = true;
                _logger.LogError(
                    "Document generation hard timeout after {Timeout} minutes. Path: {Path}, Title: {Title}, Repository: {Org}/{Repo}",
                    _options.DocumentGenerationTimeoutMinutes, item.Path, item.Title, workspace.Organization, workspace.RepositoryName);
                await LogProcessingAsync(ProcessingStep.Content, $"Document generation timed out ({_options.DocumentGenerationTimeoutMinutes}min): {item.Title}", ct);
            }
            catch (OperationCanceledException)
            {
                // Timeout occurred
                Interlocked.Increment(ref failCount);
                completionStatus = "timeout";
                shouldLogCompletion = true;
                _logger.LogError(
                    "Document generation timed out after {Timeout} minutes. Path: {Path}, Title: {Title}, Repository: {Org}/{Repo}",
                    _options.DocumentGenerationTimeoutMinutes, item.Path, item.Title, workspace.Organization, workspace.RepositoryName);
                await LogProcessingAsync(ProcessingStep.Content, $"Document generation timed out ({_options.DocumentGenerationTimeoutMinutes}min): {item.Title}", ct);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failCount);
                completionStatus = "failed";
                shouldLogCompletion = true;
                _logger.LogError(ex,
                    "Failed to generate document. Path: {Path}, Title: {Title}, Repository: {Org}/{Repo}",
                    item.Path, item.Title, workspace.Organization, workspace.RepositoryName);
                await LogProcessingAsync(ProcessingStep.Content, $"Document generation failed: {item.Title} - {ex.Message}", ct);
                // Continue with other documents - don't throw
            }
            finally
            {
                if (shouldLogCompletion)
                {
                    var completedIndex = Interlocked.Increment(ref completedCount);
                    await LogProcessingAsync(
                        ProcessingStep.Content,
                        $"Document complete ({completedIndex}/{catalogItems.Count}): {item.Title} - {completionStatus}",
                        ct);
                }
            }
        });

        stopwatch.Stop();
        _logger.LogInformation(
            "Document generation completed. Repository: {Org}/{Repo}, Language: {Language}, Success: {SuccessCount}, Failed: {FailCount}, Duration: {Duration}ms",
            workspace.Organization, workspace.RepositoryName, branchLanguage.LanguageCode,
            successCount, failCount, stopwatch.ElapsedMilliseconds);

        await LogProcessingAsync(ProcessingStep.Content, $"Document generation complete, success: {successCount}, failures: {failCount}, time: {stopwatch.ElapsedMilliseconds}ms", cancellationToken);
    }

    /// <inheritdoc />
    public async Task RegenerateDocumentAsync(
        RepositoryWorkspace workspace,
        BranchLanguage branchLanguage,
        string documentPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentPath))
        {
            throw new ArgumentException("Document path cannot be empty.", nameof(documentPath));
        }

        var normalizedPath = documentPath.Trim().Trim('/');
        var catalog = await _context.DocCatalogs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.BranchLanguageId == branchLanguage.Id &&
                     c.Path == normalizedPath &&
                     !c.IsDeleted,
                cancellationToken);

        if (catalog == null)
        {
            throw new InvalidOperationException($"Catalog not found for path: {normalizedPath}");
        }

        var catalogTitle = catalog.Title;
        var stopwatch = Stopwatch.StartNew();

        await LogProcessingAsync(
            ProcessingStep.Content,
            $"Starting regeneration of document: {catalogTitle} ({normalizedPath})",
            cancellationToken);

        await GenerateDocumentContentAsync(
            workspace,
            branchLanguage,
            normalizedPath,
            catalogTitle,
            cancellationToken);

        stopwatch.Stop();
        await LogProcessingAsync(
            ProcessingStep.Content,
            $"Document regeneration complete: {catalogTitle}，耗时 {stopwatch.ElapsedMilliseconds}ms",
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task IncrementalUpdateAsync(
        RepositoryWorkspace workspace,
        BranchLanguage branchLanguage,
        string[] changedFiles,
        CancellationToken cancellationToken = default)
    {
        if (changedFiles.Length == 0)
        {
            _logger.LogInformation(
                "No changed files, skipping incremental update. Repository: {Org}/{Repo}, Language: {Language}",
                workspace.Organization, workspace.RepositoryName, branchLanguage.LanguageCode);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Starting incremental update. Repository: {Org}/{Repo}, Language: {Language}, ChangedFileCount: {Count}",
            workspace.Organization, workspace.RepositoryName, branchLanguage.LanguageCode, changedFiles.Length);

        if (changedFiles.Length <= 50)
        {
            _logger.LogDebug("Changed files: {ChangedFiles}", string.Join(", ", changedFiles));
        }
        else
        {
            _logger.LogDebug("Changed files (first 50): {ChangedFiles}... and {More} more",
                string.Join(", ", changedFiles.Take(50)), changedFiles.Length - 50);
        }

        try
        {
            var prompt = await _promptPlugin.LoadPromptAsync(
                "incremental-updater",
                new Dictionary<string, string>
                {
                    ["repository_name"] = $"{workspace.Organization}/{workspace.RepositoryName}",
                    ["language"] = branchLanguage.LanguageCode,
                    ["previous_commit"] = workspace.PreviousCommitId ?? "initial",
                    ["current_commit"] = workspace.CommitId,
                    ["changed_files"] = string.Join("\n", changedFiles.Select(f => $"- {f}"))
                },
                cancellationToken);

            _logger.LogDebug("Initializing tools for incremental update");
            var gitTool = new GitTool(workspace.WorkingDirectory);
            var catalogStorage = new CatalogStorage(_context, branchLanguage.Id);
            var catalogTool = new CatalogTool(catalogStorage);

            var tools = gitTool.GetTools()
                .Concat(catalogTool.GetTools())
                .ToArray();

            var userMessage = $@"Please analyze code changes in repository {workspace.Organization}/{workspace.RepositoryName} and update relevant Wiki documentation.

## Change Information

- Changed Files Count: {changedFiles.Length}

## Task Requirements

1. **Analyze Change Impact**
   - Read changed files to understand modifications
   - Evaluate change types: API changes, configuration changes, new features, bug fixes, refactoring, etc.
   - Determine which documents need updating

2. **Update Strategy**
   - High Priority: Breaking API changes, new features → Must update immediately
   - Medium Priority: Behavior modifications, config changes → Update affected sections
   - Low Priority: Bug fixes, internal refactoring → Usually no documentation update needed

3. **Execute Updates**
   - Use ReadCatalog to get current catalog structure
   - Use ReadDoc to read documents that need updating
   - For minor changes, use EditDoc for precise replacements
   - For major changes, use WriteDoc to rewrite entire document
   - If new catalog items needed, use EditCatalog or WriteCatalog

4. **Quality Requirements**
   - Ensure code examples match current implementation
   - Maintain documentation language as {branchLanguage.LanguageCode}
   - Update API signatures, configuration options, usage examples, etc.

## Execution Steps

1. Read changed files and analyze change content
2. Read current catalog structure to identify affected documents
3. Read content of affected documents
4. Execute necessary document updates
5. Verify updates are complete

Please start executing the task.";

            await ExecuteAgentWithRetryAsync(
                _options.ContentModel,
                _options.GetContentRequestOptions(),
                prompt,
                userMessage,
                tools,
                "IncrementalUpdate",
                ProcessingStep.Content,
                cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "Incremental update completed successfully. Repository: {Org}/{Repo}, Language: {Language}, Duration: {Duration}ms",
                workspace.Organization, workspace.RepositoryName, branchLanguage.LanguageCode, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Incremental update failed. Repository: {Org}/{Repo}, Language: {Language}, Duration: {Duration}ms",
                workspace.Organization, workspace.RepositoryName, branchLanguage.LanguageCode, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Generates content for a single catalog item.
    /// </summary>
    private async Task GenerateDocumentContentAsync(
        RepositoryWorkspace workspace,
        BranchLanguage branchLanguage,
        string catalogPath,
        string catalogTitle,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Starting document content generation. Path: {Path}, Title: {Title}, Language: {Language}",
            catalogPath, catalogTitle, branchLanguage.LanguageCode);

        // Create a new DbContext instance for this parallel task to ensure thread safety
        using var context = _contextFactory.CreateContext();

        try
        {
            // 构建文件引用的基础URL
            var gitBaseUrl = BuildGitFileBaseUrl(workspace.GitUrl, workspace.BranchName);

            var prompt = await _promptPlugin.LoadPromptAsync(
                "content-generator",
                new Dictionary<string, string>
                {
                    ["repository_name"] = $"{workspace.Organization}/{workspace.RepositoryName}",
                    ["language"] = branchLanguage.LanguageCode,
                    ["catalog_path"] = catalogPath,
                    ["catalog_title"] = catalogTitle
                },
                cancellationToken);

            var gitTool = new GitTool(workspace.WorkingDirectory);
            var docTool = new DocTool(context, branchLanguage.Id, catalogPath, gitTool);
            var tools = await BuildToolsAsync(
                gitTool.GetTools().Concat(docTool.GetTools()),
                cancellationToken);

            var userMessage = $@"Please generate Wiki document content for catalog item ""{catalogTitle}"" (path: {catalogPath}).

## Repository Information

- Repository: {workspace.Organization}/{workspace.RepositoryName}
- Git URL: {workspace.GitUrl}
- Branch: {workspace.BranchName}
- File Reference Base URL: {gitBaseUrl}
- Target Language: {branchLanguage.LanguageCode}

## Task Requirements

1. **Gather Source Material**
   - Use ListFiles to find source files related to ""{catalogTitle}""
   - Read key implementation files and configuration files
   - Read interface definitions only when they are directly used or necessary for this document (skip unused/irrelevant interfaces)
   - Use Grep to search for related classes, functions, API endpoints

2. **Document Structure** (Must Include)
   - Title (H1): Must match catalog title
   - Overview: Explain purpose and use cases
   - Architecture Diagram: Use Mermaid to illustrate component relationships, data flow, or system architecture
   - Main Content: Detailed explanation of implementation, architecture, or usage
   - Usage Examples: Code examples extracted from actual source code
   - Configuration Options (if applicable): List options in table format
   - API Reference (if applicable): Method signatures, parameters, return values
   - Related Links: Links to related documentation and source files

3. **File Reference Links** (IMPORTANT)
   - When referencing source files, use the full URL format: {gitBaseUrl}/<file_path>
   - Example: [{gitBaseUrl}/src/Example.cs]({gitBaseUrl}/src/Example.cs)
   - For specific line references: {gitBaseUrl}/<file_path>#L<line_number>
   - Always provide clickable links to source files mentioned in the document

4. **Mermaid Diagram Requirements** (IMPORTANT)
   - Include at least one architecture or flow diagram using Mermaid
   - Use appropriate diagram types:
     * `flowchart TD` or `flowchart LR` for process flows and component relationships
     * `classDiagram` for class structures and inheritance
     * `sequenceDiagram` for interaction sequences
     * `erDiagram` for data models and relationships
     * `stateDiagram-v2` for state machines
   - Mermaid syntax rules:
     * Node IDs must not contain special characters (use letters, numbers, underscores only)
     * Use quotes for labels with special characters: `A[""Label with (parentheses)""]`
     * Escape special characters in labels properly
     * Keep diagrams focused and readable (max 15-20 nodes per diagram)
     * Use subgraph for grouping related components
   - Example valid Mermaid syntax:
     ```mermaid
     flowchart TD
         subgraph Core[""Core Components""]
             A[Service Layer] --> B[Repository]
             B --> C[(Database)]
         end
         D[API Controller] --> A
     ```

5. **Content Quality Requirements**
   - All information must be based on actual source code, do not fabricate
   - Code examples must be extracted from repository with syntax highlighting
   - Explain design intent (WHY), not just description (WHAT)
   - Write document content in {branchLanguage.LanguageCode} language
   - Keep code identifiers in original form, do not translate

6. **Output Requirements**
   - Use WriteDoc tool to write the document
   - Source files are automatically tracked from files you read

## Execution Steps

1. Analyze catalog title to determine document scope
2. Use ListFiles and Grep to find related source files
3. Read key files, extract information and code examples
4. Design appropriate Mermaid diagrams to illustrate architecture/flow
5. Organize content following document structure template
6. Ensure all file references use the correct URL format with branch
7. Call WriteDoc(content) to write document

Please start executing the task.";

            await ExecuteAgentWithRetryAsync(
                _options.ContentModel,
                _options.GetContentRequestOptions(),
                prompt,
                userMessage,
                tools,
                $"DocumentContent:{catalogPath}",
                ProcessingStep.Content,
                cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "Document content generation completed. Path: {Path}, Title: {Title}, Duration: {Duration}ms",
                catalogPath, catalogTitle, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Document content generation failed. Path: {Path}, Title: {Title}, Duration: {Duration}ms",
                catalogPath, catalogTitle, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }


    /// <summary>
    /// Combines base tools with configured Skill tools for wiki generation workflows.
    /// </summary>
    private async Task<AITool[]> BuildToolsAsync(
        IEnumerable<AITool> baseTools,
        CancellationToken cancellationToken)
    {
        var tools = baseTools.ToList();

        var enabledSkillIds = await GetEnabledSkillIdsAsync(cancellationToken);
        if (enabledSkillIds.Count == 0)
        {
            return tools.ToArray();
        }

        try
        {
            var skillTools = await _skillToolConverter.ConvertSkillConfigsToToolsAsync(
                enabledSkillIds,
                cancellationToken);

            if (skillTools.Count > 0)
            {
                tools.AddRange(skillTools);
                _logger.LogDebug("Added {SkillCount} skill tools to wiki generator", skillTools.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load skill tools for wiki generator");
        }

        return tools.ToArray();
    }

    private async Task<List<string>> GetEnabledSkillIdsAsync(CancellationToken cancellationToken)
    {
        using var context = _contextFactory.CreateContext();

        return await context.SkillConfigs
            .Where(s => s.IsActive && !s.IsDeleted)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Executes an AI agent with retry logic using exponential backoff.
    /// </summary>
    private async Task ExecuteAgentWithRetryAsync(
        string model,
        AiRequestOptions requestOptions,
        string systemPrompt,
        string userMessage,
        AITool[] tools,
        string operationName,
        ProcessingStep step,
        CancellationToken cancellationToken)
    {
        var retryCount = 0;
        Exception? lastException = null;

        _logger.LogDebug(
            "Starting AI agent execution. Operation: {Operation}, Model: {Model}, ToolCount: {ToolCount}",
            operationName, model, tools.Length);

        while (retryCount < _options.MaxRetryAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attemptStopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogDebug(
                    "AI agent attempt {Attempt}/{MaxAttempts}. Operation: {Operation}, Model: {Model}",
                    retryCount + 1, _options.MaxRetryAttempts, operationName, model);

                // Create chat options with the tools
                var chatOptions = new ChatClientAgentOptions
                {
                    ChatOptions = new ChatOptions()
                    {
                        ToolMode = ChatToolMode.Auto,
                        MaxOutputTokens = _options.MaxOutputTokens,
                        Tools = tools
                    }
                };

                // Create the chat client with tools using the AgentFactory
                var (chatClient, aiTools) = _agentFactory.CreateChatClientWithTools(
                    model,
                    tools,
                    chatOptions,
                    requestOptions);

                // Build the conversation with system prompt and user message
                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, systemPrompt),
                    new(ChatRole.User, new List<AIContent>()
                    {
                        new TextContent(userMessage),
                        new TextContent("""
                                        <system-remind>
                                        IMPORTANT REMINDERS:
                                        1. You MUST use the provided tools to complete the task. Do not just describe what you would do.
                                        2. All content must be based on actual source code from the repository. Do NOT fabricate or assume.
                                        3. After completing all tool calls, provide a brief summary of what was accomplished.
                                        4. If you encounter errors, retry with adjusted parameters or report the issue.
                                        5. Do NOT output the full document content in your response - write it using the tools instead.
                                        </system-remind>
                                        """)
                    })
                };

                // Use streaming response for real-time output
                var contentBuilder = new StringBuilder();
                UsageDetails? usageDetails = null;
                var inputTokens = 0;
                var outputTokens = 0;
                var toolCallCount = 0;

                _logger.LogDebug("Starting streaming response. Operation: {Operation}", operationName);

                var thread = await chatClient.CreateSessionAsync(cancellationToken);
                
                await foreach (var update in chatClient.RunStreamingAsync(messages, thread, cancellationToken: cancellationToken))
                {
                    // Print streaming content
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        Console.Write(update.Text);
                        contentBuilder.Append(update.Text);

                        // 记录AI输出（每100个字符记录一次，避免过于频繁）
                        if (contentBuilder.Length % 200 < update.Text.Length)
                        {
                            await LogProcessingAsync(step, update.Text, true, null, cancellationToken);
                        }
                    }

                    if (update.RawRepresentation is StreamingChatCompletionUpdate chatCompletionUpdate &&
                        chatCompletionUpdate.ToolCallUpdates.Count > 0)
                    {
                        foreach (var tool in chatCompletionUpdate.ToolCallUpdates)
                        {
                            if (!string.IsNullOrEmpty(tool.FunctionName))
                            {
                                toolCallCount++;
                                Console.WriteLine();
                                Console.Write("Call Function:" + tool.FunctionName);
                                _logger.LogDebug(
                                    "Tool call #{CallNumber}: {FunctionName}. Operation: {Operation}",
                                    toolCallCount, tool.FunctionName, operationName);

                                // 记录工具调用
                                await LogProcessingAsync(step, $"Tool call: {tool.FunctionName}", false, tool.FunctionName, cancellationToken);
                            }
                            else
                            {
                                Console.Write(" " +
                                              Encoding.UTF8.GetString(tool.FunctionArgumentsUpdate.ToArray()));
                            }
                        }
                    }

                    // Track token usage if available
                    if (update.RawRepresentation is ChatResponseUpdate chatResponseUpdate)
                    {
                        if (chatResponseUpdate.RawRepresentation is RawMessageStreamEvent
                            {
                                Value: RawMessageDeltaEvent deltaEvent
                            })
                        {
                            inputTokens = (int)((int)(deltaEvent.Usage.InputTokens ?? inputTokens) +
                                deltaEvent.Usage.CacheCreationInputTokens + deltaEvent.Usage.CacheReadInputTokens ?? 0);
                            outputTokens = (int)(deltaEvent.Usage.OutputTokens);
                        }
                    }
                    else
                    {
                        var usage = update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details;
                        if (usage != null)
                        {
                            usageDetails = usage;
                        }
                    }
                }

                // Print newline after streaming completes
                Console.WriteLine();

                attemptStopwatch.Stop();

                // Log usage statistics
                if (usageDetails != null)
                {
                    _logger.LogInformation(
                        "AI agent completed. Operation: {Operation}, Model: {Model}, InputTokens: {InputTokens}, OutputTokens: {OutputTokens}, TotalTokens: {TotalTokens}, ToolCalls: {ToolCalls}, Duration: {Duration}ms",
                        operationName, model,
                        usageDetails.InputTokenCount,
                        usageDetails.OutputTokenCount,
                        usageDetails.TotalTokenCount,
                        toolCallCount,
                        attemptStopwatch.ElapsedMilliseconds);
                }
                else if (inputTokens > 0 || outputTokens > 0)
                {
                    _logger.LogInformation(
                        "AI agent completed. Operation: {Operation}, Model: {Model}, InputTokens: {InputTokens}, OutputTokens: {OutputTokens}, TotalTokens: {TotalTokens}, ToolCalls: {ToolCalls}, Duration: {Duration}ms",
                        operationName, model,
                        inputTokens,
                        outputTokens,
                        inputTokens + outputTokens,
                        toolCallCount,
                        attemptStopwatch.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogInformation(
                        "AI agent completed. Operation: {Operation}, Model: {Model}, ToolCalls: {ToolCalls}, Duration: {Duration}ms (no usage data)",
                        operationName, model, toolCallCount, attemptStopwatch.ElapsedMilliseconds);
                }

                var recordedInputTokens = (int)(usageDetails?.InputTokenCount ?? inputTokens);
                var recordedOutputTokens = (int)(usageDetails?.OutputTokenCount ?? outputTokens);
                await RecordTokenUsageAsync(
                    recordedInputTokens,
                    recordedOutputTokens,
                    model,
                    operationName,
                    cancellationToken);

                _logger.LogDebug(
                    "Streaming response completed. Operation: {Operation}, ContentLength: {Length}",
                    operationName, contentBuilder.Length);

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && IsTransientException(ex))
            {
                attemptStopwatch.Stop();
                lastException = ex;
                retryCount++;

                _logger.LogWarning(
                    ex,
                    "AI agent attempt {Attempt}/{MaxAttempts} failed with transient error. Operation: {Operation}, Model: {Model}, Duration: {Duration}ms, ErrorType: {ErrorType}",
                    retryCount, _options.MaxRetryAttempts, operationName, model, attemptStopwatch.ElapsedMilliseconds, ex.GetType().Name);

                if (retryCount < _options.MaxRetryAttempts)
                {
                    // Exponential backoff with jitter: base_delay * 2^(attempt-1) + random(0, 1000ms)
                    var exponentialDelay = _options.RetryDelayMs * Math.Pow(2, retryCount - 1);
                    var jitter = Random.Shared.Next(0, 1000);
                    var totalDelay = (int)Math.Min(exponentialDelay + jitter, 60000); // Cap at 60 seconds

                    _logger.LogInformation(
                        "Retrying AI agent in {Delay}ms (exponential backoff). Operation: {Operation}, Attempt: {NextAttempt}/{MaxAttempts}",
                        totalDelay, operationName, retryCount + 1, _options.MaxRetryAttempts);

                    await Task.Delay(totalDelay, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Non-transient exception, don't retry
                attemptStopwatch.Stop();
                _logger.LogError(
                    ex,
                    "AI agent failed with non-transient error. Operation: {Operation}, Model: {Model}, Duration: {Duration}ms, ErrorType: {ErrorType}",
                    operationName, model, attemptStopwatch.ElapsedMilliseconds, ex.GetType().Name);
                throw;
            }
        }

        _logger.LogError(
            lastException,
            "AI agent execution failed after all retry attempts. Operation: {Operation}, Model: {Model}, Attempts: {Attempts}",
            operationName, model, _options.MaxRetryAttempts);

        throw new InvalidOperationException(
            $"AI agent execution failed after {_options.MaxRetryAttempts} attempts for operation '{operationName}'",
            lastException);
    }

    /// <summary>
    /// Determines if an exception is transient and should be retried.
    /// </summary>
    private static bool IsTransientException(Exception ex)
    {
        // Network-related exceptions
        if (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            return true;
        }

        // Check for HttpIOException (response ended prematurely)
        if (ex is System.Net.Http.HttpIOException)
        {
            return true;
        }

        // Check exception message for common transient error patterns
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("timeout") ||
               message.Contains("rate limit") ||
               message.Contains("too many requests") ||
               message.Contains("service unavailable") ||
               message.Contains("temporarily unavailable") ||
               message.Contains("connection") ||
               message.Contains("network") ||
               message.Contains("response ended prematurely");
    }

    /// <summary>
    /// Extracts all catalog paths and titles from the catalog JSON.
    /// </summary>
    private static List<(string Path, string Title)> GetAllCatalogPaths(string catalogJson)
    {
        var result = new List<(string, string)>();

        try
        {
            var root = System.Text.Json.JsonSerializer.Deserialize<CatalogRoot>(
                catalogJson,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            if (root?.Items != null)
            {
                CollectCatalogPaths(root.Items, result);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Return empty list if JSON parsing fails
        }

        return result;
    }

    /// <summary>
    /// Recursively collects catalog paths and titles.
    /// Only collects leaf nodes (nodes without children) as parent nodes are just for categorization.
    /// </summary>
    private static void CollectCatalogPaths(
        List<CatalogItem> items,
        List<(string Path, string Title)> result)
    {
        foreach (var item in items)
        {
            if (item.Children.Count > 0)
            {
                // Parent node - only for categorization, recurse into children
                CollectCatalogPaths(item.Children, result);
            }
            else
            {
                // Leaf node - needs document generation
                result.Add((item.Path, item.Title));
            }
        }
    }

    /// <summary>
    /// 记录处理日志
    /// </summary>
    private async Task LogProcessingAsync(
        ProcessingStep step,
        string message,
        bool isAiOutput = false,
        string? toolName = null,
        CancellationToken cancellationToken = default)
    {
        var repositoryId = _currentRepositoryId.Value;
        if (string.IsNullOrEmpty(repositoryId))
        {
            return;
        }

        try
        {
            await _processingLogService.LogAsync(
                repositoryId,
                step,
                message,
                isAiOutput,
                toolName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log processing step");
        }
    }

    /// <summary>
    /// 记录处理日志（简化版本）
    /// </summary>
    private Task LogProcessingAsync(ProcessingStep step, string message, CancellationToken cancellationToken)
    {
        return LogProcessingAsync(step, message, false, null, cancellationToken);
    }

    private async Task RecordTokenUsageAsync(
        int inputTokens,
        int outputTokens,
        string modelName,
        string operation,
        CancellationToken cancellationToken)
    {
        if (inputTokens <= 0 && outputTokens <= 0)
        {
            return;
        }

        try
        {
            using var context = _contextFactory.CreateContext();
            var usage = new TokenUsage
            {
                Id = Guid.NewGuid().ToString(),
                RepositoryId = _currentRepositoryId.Value,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ModelName = modelName,
                Operation = operation,
                RecordedAt = DateTime.UtcNow
            };

            context.TokenUsages.Add(usage);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record token usage. Operation: {Operation}", operation);
        }
    }

    /// <summary>
    /// Removes &lt;think&gt; tags and their content from text using compiled regex.
    /// </summary>
    private static string RemoveThinkTags(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        try
        {
            return ThinkTagRegex.Replace(text, string.Empty).Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            // If regex times out, return original text
            return text;
        }
    }

    /// <inheritdoc />
    public async Task<BranchLanguage> TranslateWikiAsync(
        RepositoryWorkspace workspace,
        BranchLanguage sourceBranchLanguage,
        string targetLanguageCode,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Starting wiki translation. Repository: {Org}/{Repo}, SourceLanguage: {SourceLang}, TargetLanguage: {TargetLang}",
            workspace.Organization, workspace.RepositoryName,
            sourceBranchLanguage.LanguageCode, targetLanguageCode);

        await LogProcessingAsync(ProcessingStep.Translation, 
            $"Starting wiki translation: {sourceBranchLanguage.LanguageCode} -> {targetLanguageCode}", cancellationToken);

        try
        {
            // 1. 创建目标语言的BranchLanguage
            var targetBranchLanguage = new BranchLanguage
            {
                Id = Guid.NewGuid().ToString(),
                RepositoryBranchId = sourceBranchLanguage.RepositoryBranchId,
                LanguageCode = targetLanguageCode.ToLowerInvariant()
            };
            _context.BranchLanguages.Add(targetBranchLanguage);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug("Created target BranchLanguage. Id: {Id}, LanguageCode: {LanguageCode}",
                targetBranchLanguage.Id, targetBranchLanguage.LanguageCode);

            // 2. 获取源语言的目录结构
            var sourceCatalogStorage = new CatalogStorage(_context, sourceBranchLanguage.Id);
            var sourceCatalogJson = await sourceCatalogStorage.GetCatalogJsonAsync(cancellationToken);

            // 3. 翻译目录结构
            await LogProcessingAsync(ProcessingStep.Translation, 
                $"Translating catalog structure -> {targetLanguageCode}", cancellationToken);

            var translatedCatalogJson = await TranslateCatalogAsync(
                sourceCatalogJson,
                sourceBranchLanguage.LanguageCode,
                targetLanguageCode,
                cancellationToken);

            // 4. 保存翻译后的目录
            var targetCatalogStorage = new CatalogStorage(_context, targetBranchLanguage.Id);
            await targetCatalogStorage.SetCatalogAsync(translatedCatalogJson, cancellationToken);

            _logger.LogInformation("Catalog translated and saved for {TargetLang}", targetLanguageCode);

            // 5. 获取所有需要翻译的文档
            var catalogItems = GetAllCatalogPaths(sourceCatalogJson);
            var totalDocs = catalogItems.Count;
            var translatedCount = 0;
            var failedCount = 0;

            await LogProcessingAsync(ProcessingStep.Translation, 
                $"发现 {totalDocs} documents to translate -> {targetLanguageCode}", cancellationToken);

            // 6. 批量预加载所有需要的数据（优化 N+1 查询）
            var catalogPaths = catalogItems.Select(i => i.Path).ToList();

            // 批量加载源语言的目录和文档
            var sourceCatalogs = await _context.DocCatalogs
                .Where(c => c.BranchLanguageId == sourceBranchLanguage.Id &&
                           catalogPaths.Contains(c.Path) &&
                           !c.IsDeleted)
                .ToListAsync(cancellationToken);

            var sourceDocFileIds = sourceCatalogs
                .Where(c => !string.IsNullOrEmpty(c.DocFileId))
                .Select(c => c.DocFileId!)
                .ToList();

            var sourceDocFiles = await _context.DocFiles
                .Where(d => sourceDocFileIds.Contains(d.Id) && !d.IsDeleted)
                .ToDictionaryAsync(d => d.Id, cancellationToken);

            // 批量加载目标语言的目录
            var targetCatalogs = await _context.DocCatalogs
                .Where(c => c.BranchLanguageId == targetBranchLanguage.Id &&
                           catalogPaths.Contains(c.Path) &&
                           !c.IsDeleted)
                .ToDictionaryAsync(c => c.Path, cancellationToken);

            // 构建翻译任务列表
            var translationTasks = new List<((string Path, string Title) Item, string SourceContent, DocCatalog TargetCatalog)>();
            foreach (var item in catalogItems)
            {
                var sourceCatalog = sourceCatalogs.FirstOrDefault(c => c.Path == item.Path);
                if (sourceCatalog == null || string.IsNullOrEmpty(sourceCatalog.DocFileId))
                {
                    _logger.LogWarning("Source document not found for path: {Path}", item.Path);
                    continue;
                }

                if (!sourceDocFiles.TryGetValue(sourceCatalog.DocFileId, out var sourceDocFile) ||
                    string.IsNullOrEmpty(sourceDocFile.Content))
                {
                    _logger.LogWarning("Source document content not found for path: {Path}", item.Path);
                    continue;
                }

                if (!targetCatalogs.TryGetValue(item.Path, out var targetCatalog))
                {
                    _logger.LogWarning("Target catalog not found for path: {Path}", item.Path);
                    continue;
                }

                translationTasks.Add((item, sourceDocFile.Content, targetCatalog));
            }

            // 7. 并行执行 AI 翻译（IO 密集型操作）
            var translationResults = new ConcurrentBag<(DocCatalog TargetCatalog, DocFile NewDocFile)?>();

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.ParallelCount,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(translationTasks, parallelOptions, async (task, ct) =>
            {
                var currentIndex = Interlocked.Increment(ref translatedCount);

                try
                {
                    await LogProcessingAsync(ProcessingStep.Translation,
                        $"Translating document ({currentIndex}/{totalDocs}): {task.Item.Title} -> {targetLanguageCode}", ct);

                    // Add timeout protection for translation
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_options.TranslationTimeoutMinutes));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                    var translatedContent = await TranslateContentAsync(
                        task.SourceContent!,
                        sourceBranchLanguage.LanguageCode,
                        targetLanguageCode,
                        linkedCts.Token);

                    // 清理 <think> 标签
                    translatedContent = RemoveThinkTags(translatedContent);

                    var newDocFile = new DocFile
                    {
                        Id = Guid.NewGuid().ToString(),
                        BranchLanguageId = targetBranchLanguage.Id,
                        Content = translatedContent
                    };

                    translationResults.Add((task.TargetCatalog!, newDocFile));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // User cancelled
                    Interlocked.Increment(ref failedCount);
                    _logger.LogWarning("Translation cancelled by user. Path: {Path}", task.Item.Path);
                    throw; // Stop the parallel loop
                }
                catch (OperationCanceledException)
                {
                    // Timeout occurred
                    Interlocked.Increment(ref failedCount);
                    _logger.LogError("Translation timed out after {Timeout} minutes. Path: {Path}, TargetLang: {TargetLang}",
                        _options.TranslationTimeoutMinutes, task.Item.Path, targetLanguageCode);
                    await LogProcessingAsync(ProcessingStep.Translation,
                        $"Document translation timed out ({_options.TranslationTimeoutMinutes}min): {task.Item.Title}", ct);
                    translationResults.Add(null);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failedCount);
                    _logger.LogError(ex, "Failed to translate document. Path: {Path}, TargetLang: {TargetLang}",
                        task.Item.Path, targetLanguageCode);
                    await LogProcessingAsync(ProcessingStep.Translation,
                        $"Document translation failed: {task.Item.Title} - {ex.Message}", ct);
                    translationResults.Add(null);
                }
            });

            // 8. 批量保存翻译结果（单线程 EF Core 操作）
            foreach (var result in translationResults.Where(r => r != null))
            {
                _context.DocFiles.Add(result!.Value.NewDocFile);
                result.Value.TargetCatalog.DocFileId = result.Value.NewDocFile.Id;
                result.Value.TargetCatalog.UpdateTimestamp();
            }

            // 9. 翻译思维导图（如果存在）
            if (!string.IsNullOrEmpty(sourceBranchLanguage.MindMapContent))
            {
                await LogProcessingAsync(ProcessingStep.Translation,
                    $"Translating mind map -> {targetLanguageCode}", cancellationToken);

                try
                {
                    var translatedMindMap = await TranslateMindMapAsync(
                        sourceBranchLanguage.MindMapContent,
                        sourceBranchLanguage.LanguageCode,
                        targetLanguageCode,
                        cancellationToken);

                    targetBranchLanguage.MindMapContent = translatedMindMap;
                    targetBranchLanguage.MindMapStatus = MindMapStatus.Completed;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to translate mind map to {TargetLang}", targetLanguageCode);
                    // 思维导图翻译failed不影响整体流程
                    targetBranchLanguage.MindMapStatus = MindMapStatus.Failed;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation(
                "Wiki translation completed. Repository: {Org}/{Repo}, TargetLanguage: {TargetLang}, Success: {Success}, Failed: {Failed}, Duration: {Duration}ms",
                workspace.Organization, workspace.RepositoryName, targetLanguageCode,
                translatedCount - failedCount, failedCount, stopwatch.ElapsedMilliseconds);

            await LogProcessingAsync(ProcessingStep.Translation, 
                $"Translation complete -> {targetLanguageCode}，success: {translatedCount - failedCount}, failures: {failedCount}, time: {stopwatch.ElapsedMilliseconds}ms", 
                cancellationToken);

            return targetBranchLanguage;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Wiki translation failed. Repository: {Org}/{Repo}, TargetLanguage: {TargetLang}, Duration: {Duration}ms",
                workspace.Organization, workspace.RepositoryName, targetLanguageCode, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Translates catalog JSON from source language to target language using AI.
    /// Uses parallel translation for better performance.
    /// </summary>
    private async Task<string> TranslateCatalogAsync(
        string sourceCatalogJson,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        // 解析源目录结构
        var root = System.Text.Json.JsonSerializer.Deserialize<CatalogRoot>(sourceCatalogJson, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        if (root?.Items == null || root.Items.Count == 0)
        {
            return sourceCatalogJson;
        }

        // 收集所有需要翻译的项（扁平化）
        var allItems = new List<CatalogItem>();
        CollectAllCatalogItems(root.Items, allItems);

        _logger.LogDebug("Collected {Count} catalog items for parallel translation", allItems.Count);

        // 并发翻译所有标题
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.ParallelCount,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(allItems, parallelOptions, async (item, ct) =>
        {
            try
            {
                // Add timeout protection for title translation
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(_options.TitleTranslationTimeoutMinutes));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var translatedTitle = await TranslateSingleTitleAsync(
                    item.Title, sourceLanguage, targetLanguage, linkedCts.Token);
                item.Title = translatedTitle;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Catalog title translation cancelled. Title: {Title}", item.Title);
                throw; // Stop the parallel loop
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Catalog title translation timed out. Title: {Title}", item.Title);
                // Keep original title on timeout
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to translate catalog title. Title: {Title}", item.Title);
                // Keep original title on error
            }
        });

        // 序列化回 JSON
        return System.Text.Json.JsonSerializer.Serialize(root, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Recursively collects all catalog items into a flat list for parallel processing.
    /// </summary>
    private static void CollectAllCatalogItems(List<CatalogItem> items, List<CatalogItem> result)
    {
        foreach (var item in items)
        {
            result.Add(item);
            if (item.Children.Count > 0)
            {
                CollectAllCatalogItems(item.Children, result);
            }
        }
    }

    /// <summary>
    /// Translates a single title string using AI (synchronous request).
    /// </summary>
    private async Task<string> TranslateSingleTitleAsync(
        string title,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        var prompt = $@"Translate the following title from {sourceLanguage} to {targetLanguage}. Return ONLY the translated text, nothing else.

Title: {title}

Translation:";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var chatClient = _agentFactory.CreateSimpleChatClient(
            _options.GetTranslationModel(),
            _options.MaxOutputTokens,
            _options.GetTranslationRequestOptions());
        var thread = await chatClient.CreateSessionAsync(cancellationToken);

        var contentBuilder = new StringBuilder();
        var inputTokens = 0;
        var outputTokens = 0;

        await foreach (var update in chatClient.RunStreamingAsync(messages, thread, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                contentBuilder.Append(update.Text);
            }

            if (update.RawRepresentation is ChatResponseUpdate chatResponseUpdate)
            {
                if (chatResponseUpdate.RawRepresentation is RawMessageStreamEvent
                    {
                        Value: RawMessageDeltaEvent deltaEvent
                    })
                {
                    inputTokens = (int)((int)(deltaEvent.Usage.InputTokens ?? inputTokens) +
                        deltaEvent.Usage.CacheCreationInputTokens + deltaEvent.Usage.CacheReadInputTokens ?? 0);
                    outputTokens = (int)(deltaEvent.Usage.OutputTokens);
                }
            }
            else
            {
                var usage = update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details;
                if (usage != null)
                {
                    inputTokens = (int)(usage.InputTokenCount ?? inputTokens);
                    outputTokens = (int)(usage.OutputTokenCount ?? outputTokens);
                }
            }
        }

        var translatedTitle = contentBuilder.ToString().Trim();

        if (inputTokens > 0 || outputTokens > 0)
        {
            _logger.LogDebug(
                "TranslateSingleTitleAsync token usage. InputTokens: {InputTokens}, OutputTokens: {OutputTokens}, TotalTokens: {TotalTokens}",
                inputTokens,
                outputTokens,
                inputTokens + outputTokens);
        }

        await RecordTokenUsageAsync(
            inputTokens,
            outputTokens,
            _options.GetTranslationModel(),
            "Translation:CatalogTitle",
            cancellationToken);

        // 清理可能的<think>标签及其内容
        translatedTitle = RemoveThinkTags(translatedTitle);

        // 如果翻译failed或返回空，保留原标题
        return string.IsNullOrWhiteSpace(translatedTitle) ? title : translatedTitle;
    }

    /// <summary>
    /// Translates markdown content from source language to target language using AI.
    /// </summary>
    private async Task<string> TranslateContentAsync(
        string sourceContent,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var prompt = $@"You are a professional technical documentation translator. Translate the following Markdown document from {sourceLanguage} to {targetLanguage}.

IMPORTANT RULES:
1. Translate all text content to {targetLanguage}
2. Keep all code blocks, code snippets, and technical identifiers unchanged
3. Keep all URLs, file paths, and variable names unchanged
4. Maintain the exact Markdown formatting (headers, lists, tables, etc.)
5. Keep inline code (`code`) unchanged
6. Translate comments inside code blocks if they are in {sourceLanguage}
7. Return only the translated Markdown, no explanations

Source document:
{sourceContent}

Translated document:";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var retryCount = 0;
        Exception? lastException = null;

        while (retryCount < _options.MaxRetryAttempts)
        {
            try
            {
                var chatClient = _agentFactory.CreateSimpleChatClient(
                    _options.GetTranslationModel(), 
                    _options.MaxOutputTokens, 
                    _options.GetTranslationRequestOptions());
                var thread = await chatClient.CreateSessionAsync(cancellationToken);
                
                var contentBuilder = new StringBuilder();
                var inputTokens = 0;
                var outputTokens = 0;
                await foreach (var update in chatClient.RunStreamingAsync(messages, thread, cancellationToken: cancellationToken))
                {
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        contentBuilder.Append(update.Text);
                    }

                    // Track token usage if available
                    if (update.RawRepresentation is ChatResponseUpdate chatResponseUpdate)
                    {
                        if (chatResponseUpdate.RawRepresentation is RawMessageStreamEvent
                            {
                                Value: RawMessageDeltaEvent deltaEvent
                            })
                        {
                            inputTokens = (int)((int)(deltaEvent.Usage.InputTokens ?? inputTokens) +
                                deltaEvent.Usage.CacheCreationInputTokens + deltaEvent.Usage.CacheReadInputTokens ?? 0);
                            outputTokens = (int)(deltaEvent.Usage.OutputTokens);
                        }
                    }
                    else
                    {
                        var usage = update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details;
                        if (usage != null)
                        {
                            inputTokens = (int)(usage.InputTokenCount ?? inputTokens);
                            outputTokens = (int)(usage.OutputTokenCount ?? outputTokens);
                        }
                    }
                }

                var result = contentBuilder.ToString().Trim();
                if (inputTokens > 0 || outputTokens > 0)
                {
                    _logger.LogDebug(
                        "TranslateContentAsync token usage. InputTokens: {InputTokens}, OutputTokens: {OutputTokens}, TotalTokens: {TotalTokens}",
                        inputTokens,
                        outputTokens,
                        inputTokens + outputTokens);
                }

                await RecordTokenUsageAsync(
                    inputTokens,
                    outputTokens,
                    _options.GetTranslationModel(),
                    "Translation:Content",
                    cancellationToken);

                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && IsTransientException(ex))
            {
                lastException = ex;
                retryCount++;

                _logger.LogWarning(ex,
                    "TranslateContentAsync attempt {Attempt}/{MaxAttempts} failed with transient error. SourceLang: {SourceLang}, TargetLang: {TargetLang}",
                    retryCount, _options.MaxRetryAttempts, sourceLanguage, targetLanguage);

                if (retryCount < _options.MaxRetryAttempts)
                {
                    var delay = _options.RetryDelayMs * Math.Pow(2, retryCount - 1) + Random.Shared.Next(0, 1000);
                    await Task.Delay((int)Math.Min(delay, 60000), cancellationToken);
                }
            }
        }

        throw new InvalidOperationException(
            $"TranslateContentAsync failed after {_options.MaxRetryAttempts} attempts",
            lastException);
    }

    /// <summary>
    /// Translates mind map content from source language to target language using AI.
    /// Preserves the hierarchical format (# ## ###) and file paths.
    /// </summary>
    private async Task<string> TranslateMindMapAsync(
        string sourceMindMap,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var prompt = $@"You are a professional technical documentation translator. Translate the following mind map content from {sourceLanguage} to {targetLanguage}.

IMPORTANT RULES:
1. Translate only the title text (before the colon if present)
2. Keep the hierarchical format (# ## ###) exactly as is
3. Keep all file paths (after the colon) unchanged
4. Keep the line structure unchanged
5. Return only the translated mind map, no explanations

Example:
Input:
# 系统架构
## 前端应用:web/app
## 后端服务:src/Api

Output (if translating to English):
# System Architecture
## Frontend Application:web/app
## Backend Service:src/Api

Source mind map:
{sourceMindMap}

Translated mind map:";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var retryCount = 0;
        Exception? lastException = null;

        while (retryCount < _options.MaxRetryAttempts)
        {
            try
            {
                var chatClient = _agentFactory.CreateSimpleChatClient(
                    _options.GetTranslationModel(),
                    _options.MaxOutputTokens,
                    _options.GetTranslationRequestOptions());
                var thread = await chatClient.CreateSessionAsync(cancellationToken);

                var contentBuilder = new StringBuilder();
                var inputTokens = 0;
                var outputTokens = 0;
                await foreach (var update in chatClient.RunStreamingAsync(messages, thread, cancellationToken: cancellationToken))
                {
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        contentBuilder.Append(update.Text);
                    }

                    // Track token usage if available
                    if (update.RawRepresentation is ChatResponseUpdate chatResponseUpdate)
                    {
                        if (chatResponseUpdate.RawRepresentation is RawMessageStreamEvent
                            {
                                Value: RawMessageDeltaEvent deltaEvent
                            })
                        {
                            inputTokens = (int)((int)(deltaEvent.Usage.InputTokens ?? inputTokens) +
                                deltaEvent.Usage.CacheCreationInputTokens + deltaEvent.Usage.CacheReadInputTokens ?? 0);
                            outputTokens = (int)(deltaEvent.Usage.OutputTokens);
                        }
                    }
                    else
                    {
                        var usage = update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details;
                        if (usage != null)
                        {
                            inputTokens = (int)(usage.InputTokenCount ?? inputTokens);
                            outputTokens = (int)(usage.OutputTokenCount ?? outputTokens);
                        }
                    }
                }

                var result = contentBuilder.ToString().Trim();

                // 清理可能的 <think> 标签
                result = RemoveThinkTags(result);

                if (inputTokens > 0 || outputTokens > 0)
                {
                    _logger.LogDebug(
                        "TranslateMindMapAsync token usage. InputTokens: {InputTokens}, OutputTokens: {OutputTokens}, TotalTokens: {TotalTokens}",
                        inputTokens,
                        outputTokens,
                        inputTokens + outputTokens);
                }

                await RecordTokenUsageAsync(
                    inputTokens,
                    outputTokens,
                    _options.GetTranslationModel(),
                    "Translation:MindMap",
                    cancellationToken);

                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && IsTransientException(ex))
            {
                lastException = ex;
                retryCount++;

                _logger.LogWarning(ex,
                    "TranslateMindMapAsync attempt {Attempt}/{MaxAttempts} failed with transient error. SourceLang: {SourceLang}, TargetLang: {TargetLang}",
                    retryCount, _options.MaxRetryAttempts, sourceLanguage, targetLanguage);

                if (retryCount < _options.MaxRetryAttempts)
                {
                    var delay = _options.RetryDelayMs * Math.Pow(2, retryCount - 1) + Random.Shared.Next(0, 1000);
                    await Task.Delay((int)Math.Min(delay, 60000), cancellationToken);
                }
            }
        }

        throw new InvalidOperationException(
            $"TranslateMindMapAsync failed after {_options.MaxRetryAttempts} attempts",
            lastException);
    }

    /// <summary>
    /// Collects comprehensive repository context including directory structure, 
    /// project type detection, and README content.
    /// </summary>
    private async Task<RepositoryContext> CollectRepositoryContextAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var context = new RepositoryContext();
            var rootDir = new DirectoryInfo(workingDirectory);
            
            var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "node_modules", "bin", "obj", "dist", "build", ".git", ".svn", ".hg",
                ".idea", ".vscode", ".vs", "__pycache__", ".cache", "coverage",
                "packages", "vendor", ".next", ".nuxt", "target", "out", ".output"
            };

            // 1. Detect project type
            context.ProjectType = DetectProjectType(rootDir);
            
            // 2. Collect directory structure as tree data and serialize with Toon
            var treeData = CollectDirectoryTreeData(rootDir, excludedDirs, maxDepth: _options.DirectoryTreeMaxDepth, currentDepth: 0);
            context.DirectoryTree = ToonSerializer.Serialize(treeData);
            
            // 3. Read README content (truncated if too long)
            context.ReadmeContent = ReadReadmeContent(rootDir, _options.ReadmeMaxLength);
            
            // 4. Collect key configuration files
            context.KeyFiles = CollectKeyFiles(rootDir, context.ProjectType);
            
            // 5. Identify entry points based on project type
            context.EntryPoints = IdentifyEntryPoints(rootDir, context.ProjectType);
            
            return context;
        }, cancellationToken);
    }

    /// <summary>
    /// Detects the project type based on configuration files present.
    /// </summary>
    private static string DetectProjectType(DirectoryInfo rootDir)
    {
        var types = new List<string>();
        
        // Check for various project types
        if (rootDir.GetFiles("*.csproj", SearchOption.AllDirectories).Any() ||
            rootDir.GetFiles("*.sln").Any())
            types.Add("dotnet");
            
        if (File.Exists(Path.Combine(rootDir.FullName, "package.json")))
        {
            var packageJson = File.ReadAllText(Path.Combine(rootDir.FullName, "package.json"));
            if (packageJson.Contains("\"next\"") || packageJson.Contains("\"react\"") || 
                packageJson.Contains("\"vue\"") || packageJson.Contains("\"angular\""))
                types.Add("frontend");
            else
                types.Add("nodejs");
        }
        
        if (File.Exists(Path.Combine(rootDir.FullName, "pom.xml")) ||
            rootDir.GetFiles("build.gradle*").Any())
            types.Add("java");
            
        if (File.Exists(Path.Combine(rootDir.FullName, "go.mod")))
            types.Add("go");
            
        if (File.Exists(Path.Combine(rootDir.FullName, "Cargo.toml")))
            types.Add("rust");
            
        if (File.Exists(Path.Combine(rootDir.FullName, "requirements.txt")) ||
            File.Exists(Path.Combine(rootDir.FullName, "pyproject.toml")) ||
            File.Exists(Path.Combine(rootDir.FullName, "setup.py")))
            types.Add("python");

        if (types.Count == 0)
            return "unknown";
        if (types.Count > 1)
            return "fullstack:" + string.Join("+", types);
        return types[0];
    }

    /// <summary>
    /// Collects directory tree structure as a data object for Toon serialization.
    /// </summary>
    private static List<FileTreeNode> CollectDirectoryTreeData(
        DirectoryInfo dir,
        HashSet<string> excludedDirs,
        int maxDepth,
        int currentDepth)
    {
        var result = new List<FileTreeNode>();
        if (currentDepth > maxDepth) return result;

        try
        {
            // Get directories
            foreach (var subDir in dir.GetDirectories().OrderBy(d => d.Name))
            {
                if (subDir.Name.StartsWith('.') || excludedDirs.Contains(subDir.Name))
                    continue;

                var node = new FileTreeNode
                {
                    Name = subDir.Name,
                    Type = "dir"
                };

                if (currentDepth < maxDepth)
                {
                    node.Children = CollectDirectoryTreeData(subDir, excludedDirs, maxDepth, currentDepth + 1);
                }

                result.Add(node);
            }

            // Get files (only at depth 0 and 1 to reduce noise)
            if (currentDepth <= 1)
            {
                foreach (var file in dir.GetFiles().OrderBy(f => f.Name))
                {
                    if (file.Name.StartsWith('.'))
                        continue;

                    result.Add(new FileTreeNode
                    {
                        Name = file.Name,
                        Type = "file"
                    });
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip inaccessible directories
        }

        return result;
    }

    /// <summary>
    /// Represents a node in the file tree structure.
    /// </summary>
    private class FileTreeNode
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "file"; // "file" or "dir"
        public List<FileTreeNode>? Children { get; set; }
    }

    /// <summary>
    /// Reads README content, truncated if too long.
    /// </summary>
    private static string ReadReadmeContent(DirectoryInfo rootDir, int maxLength)
    {
        var readmeNames = new[] { "README.md", "README.MD", "readme.md", "README.rst", "README.txt", "README" };

        foreach (var name in readmeNames)
        {
            var path = Path.Combine(rootDir.FullName, name);
            if (File.Exists(path))
            {
                try
                {
                    var content = File.ReadAllText(path);
                    // Truncate if too long
                    if (content.Length > maxLength)
                    {
                        content = content[..maxLength] + "\n\n[... README truncated for brevity ...]";
                    }
                    return content;
                }
                catch
                {
                    return "[Unable to read README]";
                }
            }
        }
        return "[No README found]";
    }

    /// <summary>
    /// Collects key configuration files based on project type.
    /// </summary>
    private static List<string> CollectKeyFiles(DirectoryInfo rootDir, string projectType)
    {
        var keyFiles = new List<string>();
        var commonFiles = new[] 
        { 
            "package.json", "tsconfig.json", "docker-compose.yml", "Dockerfile",
            "Makefile", ".env.example", "appsettings.json"
        };
        
        foreach (var file in commonFiles)
        {
            if (File.Exists(Path.Combine(rootDir.FullName, file)))
                keyFiles.Add(file);
        }
        
        // Add project-specific files
        if (projectType.Contains("dotnet"))
        {
            keyFiles.AddRange(rootDir.GetFiles("*.sln").Select(f => f.Name));
            keyFiles.AddRange(rootDir.GetFiles("*.csproj", SearchOption.TopDirectoryOnly).Select(f => f.Name));
        }
        
        if (projectType.Contains("java"))
        {
            if (File.Exists(Path.Combine(rootDir.FullName, "pom.xml")))
                keyFiles.Add("pom.xml");
        }
        
        if (projectType.Contains("go"))
        {
            keyFiles.Add("go.mod");
        }
        
        if (projectType.Contains("python"))
        {
            var pyFiles = new[] { "requirements.txt", "pyproject.toml", "setup.py" };
            keyFiles.AddRange(pyFiles.Where(f => File.Exists(Path.Combine(rootDir.FullName, f))));
        }
        
        return keyFiles.Distinct().ToList();
    }

    /// <summary>
    /// Identifies likely entry point files based on project type.
    /// </summary>
    private static List<string> IdentifyEntryPoints(DirectoryInfo rootDir, string projectType)
    {
        var entryPoints = new List<string>();
        
        if (projectType.Contains("dotnet"))
        {
            // Find Program.cs, Startup.cs
            var dotnetEntries = new[] { "Program.cs", "Startup.cs" };
            foreach (var entry in dotnetEntries)
            {
                var files = rootDir.GetFiles(entry, SearchOption.AllDirectories)
                    .Where(f => !f.FullName.Contains("bin") && !f.FullName.Contains("obj"))
                    .Take(3);
                entryPoints.AddRange(files.Select(f => Path.GetRelativePath(rootDir.FullName, f.FullName).Replace('\\', '/')));
            }
        }
        
        if (projectType.Contains("frontend") || projectType.Contains("nodejs"))
        {
            // Find index.tsx, App.tsx, main.ts, etc.
            var frontendEntries = new[] { "index.tsx", "index.ts", "App.tsx", "App.vue", "main.ts", "main.tsx" };
            foreach (var entry in frontendEntries)
            {
                var files = rootDir.GetFiles(entry, SearchOption.AllDirectories)
                    .Where(f => !f.FullName.Contains("node_modules"))
                    .Take(2);
                entryPoints.AddRange(files.Select(f => Path.GetRelativePath(rootDir.FullName, f.FullName).Replace('\\', '/')));
            }
        }
        
        if (projectType.Contains("python"))
        {
            var pyEntries = new[] { "main.py", "app.py", "manage.py", "__main__.py" };
            foreach (var entry in pyEntries)
            {
                var files = rootDir.GetFiles(entry, SearchOption.AllDirectories).Take(2);
                entryPoints.AddRange(files.Select(f => Path.GetRelativePath(rootDir.FullName, f.FullName).Replace('\\', '/')));
            }
        }
        
        if (projectType.Contains("go"))
        {
            var files = rootDir.GetFiles("main.go", SearchOption.AllDirectories).Take(3);
            entryPoints.AddRange(files.Select(f => Path.GetRelativePath(rootDir.FullName, f.FullName).Replace('\\', '/')));
        }
        
        return entryPoints.Distinct().Take(10).ToList();
    }

    /// <summary>
    /// Repository context information for catalog generation.
    /// </summary>
    private class RepositoryContext
    {
        public string ProjectType { get; set; } = "unknown";
        public string DirectoryTree { get; set; } = "";
        public string ReadmeContent { get; set; } = "";
        public List<string> KeyFiles { get; set; } = new();
        public List<string> EntryPoints { get; set; } = new();
    }

    /// <summary>
    /// Builds the base URL for file references in the repository.
    /// Supports GitHub, GitLab, Gitee, Bitbucket, and Azure DevOps URL formats.
    /// </summary>
    /// <param name="gitUrl">The Git repository URL (can be HTTPS or SSH format)</param>
    /// <param name="branchName">The branch name</param>
    /// <returns>The base URL for file references</returns>
    private static string BuildGitFileBaseUrl(string gitUrl, string branchName)
    {
        if (string.IsNullOrEmpty(gitUrl))
        {
            return string.Empty;
        }

        // Normalize the URL - remove .git suffix and convert SSH to HTTPS format
        var normalizedUrl = gitUrl
            .Replace(".git", "")
            .Trim();

        // Convert SSH format to HTTPS format
        // git@github.com:owner/repo -> https://github.com/owner/repo
        if (normalizedUrl.StartsWith("git@"))
        {
            normalizedUrl = normalizedUrl
                .Replace("git@", "https://")
                .Replace(":", "/");
        }

        // Remove trailing slash if present
        normalizedUrl = normalizedUrl.TrimEnd('/');

        // Determine the platform and build appropriate URL
        if (normalizedUrl.Contains("github.com"))
        {
            // GitHub: https://github.com/owner/repo/blob/branch/path
            return $"{normalizedUrl}/blob/{branchName}";
        }
        else if (normalizedUrl.Contains("gitlab.com") || normalizedUrl.Contains("gitlab"))
        {
            // GitLab: https://gitlab.com/owner/repo/-/blob/branch/path
            return $"{normalizedUrl}/-/blob/{branchName}";
        }
        else if (normalizedUrl.Contains("gitee.com"))
        {
            // Gitee: https://gitee.com/owner/repo/blob/branch/path
            return $"{normalizedUrl}/blob/{branchName}";
        }
        else if (normalizedUrl.Contains("bitbucket.org"))
        {
            // Bitbucket: https://bitbucket.org/owner/repo/src/branch/path
            return $"{normalizedUrl}/src/{branchName}";
        }
        else if (normalizedUrl.Contains("dev.azure.com") || normalizedUrl.Contains("visualstudio.com"))
        {
            // Azure DevOps: https://dev.azure.com/org/project/_git/repo?path=/path&version=GBbranch
            // Simplified format for file links
            return $"{normalizedUrl}?version=GB{branchName}&path=";
        }
        else
        {
            // Default: assume GitHub-like format
            return $"{normalizedUrl}/blob/{branchName}";
        }
    }
}
