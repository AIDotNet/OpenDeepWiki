using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic.Models.Messages;
using LibGit2Sharp;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Agents;
using OpenDeepWiki.Agents.Tools;
using OpenDeepWiki.Chat.Exceptions;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;
using GitRepository = LibGit2Sharp.Repository;

namespace OpenDeepWiki.Services.Chat;

/// <summary>
/// DTO for embed configuration response.
/// </summary>
public class EmbedConfigDto
{
    public bool Valid { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? AppName { get; set; }
    public string? IconUrl { get; set; }
    public List<string> AvailableModels { get; set; } = new();
    public string? DefaultModel { get; set; }
}

/// <summary>
/// DTO for embed chat request.
/// </summary>
public class EmbedChatRequest
{
    public string AppId { get; set; } = string.Empty;
    public List<ChatMessageDto> Messages { get; set; } = new();
    public string? ModelId { get; set; }
    public string? UserIdentifier { get; set; }

    /// <summary>
    /// Repository owner (e.g., "microsoft")
    /// </summary>
    public string? Owner { get; set; }

    /// <summary>
    /// Repository name (e.g., "vscode")
    /// </summary>
    public string? Repo { get; set; }

    /// <summary>
    /// Branch name (e.g., "main")
    /// </summary>
    public string? Branch { get; set; }
}

/// <summary>
/// Interface for embed service.
/// </summary>
public interface IEmbedService
{
    /// <summary>
    /// Validates if the AppId is valid and active.
    /// </summary>
    Task<(bool IsValid, string? ErrorCode, string? ErrorMessage)> ValidateAppAsync(
        string appId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if the request domain is allowed for the app.
    /// </summary>
    Task<(bool IsValid, string? ErrorCode, string? ErrorMessage)> ValidateDomainAsync(
        string appId,
        string? domain,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the app configuration for embedding.
    /// </summary>
    Task<EmbedConfigDto> GetAppConfigAsync(
        string appId,
        string? domain,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams chat responses for embedded widget.
    /// </summary>
    IAsyncEnumerable<SSEEvent> StreamEmbedChatAsync(
        EmbedChatRequest request,
        string? sourceDomain,
        CancellationToken cancellationToken = default);
}


/// <summary>
/// Embed service implementation.
/// Provides validation and chat functionality for embedded widgets.
/// </summary>
public class EmbedService : IEmbedService
{
    private readonly IContext _context;
    private readonly IContextFactory _contextFactory;
    private readonly IChatAppService _chatAppService;
    private readonly IAppStatisticsService _statisticsService;
    private readonly IChatLogService _chatLogService;
    private readonly AgentFactory _agentFactory;
    private readonly RepositoryAnalyzerOptions _repoOptions;
    private readonly ILogger<EmbedService> _logger;

    public EmbedService(
        IContext context,
        IContextFactory contextFactory,
        IChatAppService chatAppService,
        IAppStatisticsService statisticsService,
        IChatLogService chatLogService,
        AgentFactory agentFactory,
        IOptions<RepositoryAnalyzerOptions> repoOptions,
        ILogger<EmbedService> logger)
    {
        _context = context;
        _contextFactory = contextFactory;
        _chatAppService = chatAppService;
        _statisticsService = statisticsService;
        _chatLogService = chatLogService;
        _agentFactory = agentFactory;
        _repoOptions = repoOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(bool IsValid, string? ErrorCode, string? ErrorMessage)> ValidateAppAsync(
        string appId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return (false, "INVALID_APP_ID", "AppId cannot be empty");
        }

        var app = await _chatAppService.GetAppByAppIdAsync(appId, cancellationToken);

        if (app == null)
        {
            return (false, "INVALID_APP_ID", "Application not found");
        }

        if (!app.IsActive)
        {
            return (false, "APP_INACTIVE", "Application is inactive");
        }

        // Check if AI configuration is complete
        if (string.IsNullOrWhiteSpace(app.ApiKey))
        {
            return (false, "CONFIG_MISSING", "Application API key not configured");
        }

        if (app.AvailableModels.Count == 0 && string.IsNullOrWhiteSpace(app.DefaultModel))
        {
            return (false, "CONFIG_MISSING", "No available models configured for application");
        }

        return (true, null, null);
    }

    /// <inheritdoc />
    public async Task<(bool IsValid, string? ErrorCode, string? ErrorMessage)> ValidateDomainAsync(
        string appId,
        string? domain,
        CancellationToken cancellationToken = default)
    {
        var app = await _chatAppService.GetAppByAppIdAsync(appId, cancellationToken);

        if (app == null)
        {
            return (false, "INVALID_APP_ID", "Application not found");
        }

        // If domain validation is not enabled, allow all domains
        if (!app.EnableDomainValidation)
        {
            return (true, null, null);
        }

        // If domain validation is enabled but no domain provided
        if (string.IsNullOrWhiteSpace(domain))
        {
            return (false, "DOMAIN_NOT_ALLOWED", "Unable to determine request origin domain");
        }

        // Check if domain is in allowed list
        if (app.AllowedDomains.Count == 0)
        {
            return (false, "DOMAIN_NOT_ALLOWED", "No allowed domains configured");
        }

        var normalizedDomain = NormalizeDomain(domain);
        var isAllowed = app.AllowedDomains.Any(d => IsDomainMatch(normalizedDomain, d));

        if (!isAllowed)
        {
            return (false, "DOMAIN_NOT_ALLOWED", $"Domain {domain} is not in the allowed list");
        }

        return (true, null, null);
    }

    /// <inheritdoc />
    public async Task<EmbedConfigDto> GetAppConfigAsync(
        string appId,
        string? domain,
        CancellationToken cancellationToken = default)
    {
        // Validate AppId
        var (isAppValid, appErrorCode, appErrorMessage) = await ValidateAppAsync(appId, cancellationToken);
        if (!isAppValid)
        {
            return new EmbedConfigDto
            {
                Valid = false,
                ErrorCode = appErrorCode,
                ErrorMessage = appErrorMessage
            };
        }

        // Validate domain
        var (isDomainValid, domainErrorCode, domainErrorMessage) = await ValidateDomainAsync(appId, domain, cancellationToken);
        if (!isDomainValid)
        {
            return new EmbedConfigDto
            {
                Valid = false,
                ErrorCode = domainErrorCode,
                ErrorMessage = domainErrorMessage
            };
        }

        var app = await _chatAppService.GetAppByAppIdAsync(appId, cancellationToken);
        if (app == null)
        {
            return new EmbedConfigDto
            {
                Valid = false,
                ErrorCode = "INVALID_APP_ID",
                ErrorMessage = "Application not found"
            };
        }

        return new EmbedConfigDto
        {
            Valid = true,
            AppName = app.Name,
            IconUrl = app.IconUrl,
            AvailableModels = app.AvailableModels,
            DefaultModel = app.DefaultModel
        };
    }


    /// <inheritdoc />
    public async IAsyncEnumerable<SSEEvent> StreamEmbedChatAsync(
        EmbedChatRequest request,
        string? sourceDomain,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Validate AppId
        var (isAppValid, appErrorCode, appErrorMessage) = await ValidateAppAsync(request.AppId, cancellationToken);
        if (!isAppValid)
        {
            yield return new SSEEvent
            {
                Type = SSEEventType.Error,
                Data = SSEErrorResponse.CreateNonRetryable(appErrorCode!, appErrorMessage)
            };
            yield break;
        }

        // Validate domain
        var (isDomainValid, domainErrorCode, domainErrorMessage) = await ValidateDomainAsync(request.AppId, sourceDomain, cancellationToken);
        if (!isDomainValid)
        {
            yield return new SSEEvent
            {
                Type = SSEEventType.Error,
                Data = SSEErrorResponse.CreateNonRetryable(domainErrorCode!, domainErrorMessage)
            };
            yield break;
        }

        // Get app configuration
        var app = await _chatAppService.GetAppByAppIdAsync(request.AppId, cancellationToken);
        if (app == null)
        {
            yield return new SSEEvent
            {
                Type = SSEEventType.Error,
                Data = SSEErrorResponse.CreateNonRetryable(
                    ChatErrorCodes.INVALID_APP_ID,
                    "Application not found")
            };
            yield break;
        }

        // Determine model to use
        var modelId = request.ModelId;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            modelId = app.DefaultModel;
        }

        // Validate model is in available models
        if (!string.IsNullOrWhiteSpace(modelId) && app.AvailableModels.Count > 0 && !app.AvailableModels.Contains(modelId))
        {
            yield return new SSEEvent
            {
                Type = SSEEventType.Error,
                Data = SSEErrorResponse.CreateNonRetryable(
                    ChatErrorCodes.MODEL_UNAVAILABLE,
                    "Selected model is unavailable")
            };
            yield break;
        }

        // Use default model if none specified
        if (string.IsNullOrWhiteSpace(modelId))
        {
            modelId = app.AvailableModels.FirstOrDefault() ?? "gpt-4o-mini";
        }

        // Calculate repository path from Owner/Repo
        GitTool? gitTool = null;
        var tools = new List<AITool>();

        if (!string.IsNullOrWhiteSpace(request.Owner) && !string.IsNullOrWhiteSpace(request.Repo))
        {
            var repositoryPath = GetRepositoryPath(request.Owner, request.Repo);
            if (Directory.Exists(repositoryPath))
            {
                try
                {
                    // Checkout to the specified branch before initializing GitTool
                    if (!string.IsNullOrWhiteSpace(request.Branch))
                    {
                        CheckoutBranch(repositoryPath, request.Branch);
                    }
                    
                    gitTool = new GitTool(repositoryPath);
                    tools.AddRange(gitTool.GetTools());
                    _logger.LogInformation("GitTool initialized for app {AppId} with repository {Owner}/{Repo}@{Branch}",
                        request.AppId, request.Owner, request.Repo, request.Branch);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to initialize GitTool for app {AppId}", request.AppId);
                }
            }
            else
            {
                _logger.LogWarning("Repository path does not exist: {RepoPath}", repositoryPath);
            }
        }

        // Build enhanced system prompt with repository context
        var systemPrompt = BuildEnhancedSystemPrompt(
            app.Name,
            app.Description,
            request.Owner,
            request.Repo,
            gitTool != null);

        // Create agent with app's AI configuration
        var agentOptions = new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 32000
            }
        };

        var requestOptions = new AiRequestOptions
        {
            ApiKey = app.ApiKey,
            Endpoint = app.BaseUrl,
            RequestType = ParseRequestType(app.ProviderType)
        };

        var (agent, _) = _agentFactory.CreateChatClientWithTools(
            modelId,
            tools.ToArray(),
            agentOptions,
            requestOptions);

        // Build chat messages
        var chatMessages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt)
        };
        chatMessages.AddRange(BuildChatMessages(request.Messages));

        // Get the last user message for logging
        var lastUserMessage = request.Messages.LastOrDefault(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
        var question = lastUserMessage?.Content ?? string.Empty;

        // Stream response
        var inputTokens = 0;
        var outputTokens = 0;
        var responseBuilder = new System.Text.StringBuilder();

        var thread = await agent.CreateSessionAsync(cancellationToken);

        await foreach (var update in agent.RunStreamingAsync(chatMessages, thread, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                responseBuilder.Append(update.Text);
                yield return new SSEEvent
                {
                    Type = SSEEventType.Content,
                    Data = update.Text
                };
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

        // Record statistics
        await _statisticsService.RecordRequestAsync(new RecordRequestDto
        {
            AppId = request.AppId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        }, cancellationToken);

        await RecordTokenUsageAsync(
            inputTokens,
            outputTokens,
            modelId,
            request.Owner,
            request.Repo,
            cancellationToken);

        // Record chat log
        var answerSummary = responseBuilder.Length > 500
            ? responseBuilder.ToString(0, 500) + "..."
            : responseBuilder.ToString();

        await _chatLogService.RecordChatLogAsync(new RecordChatLogDto
        {
            AppId = request.AppId,
            UserIdentifier = request.UserIdentifier,
            Question = question,
            AnswerSummary = answerSummary,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ModelUsed = modelId,
            SourceDomain = sourceDomain
        }, cancellationToken);

        // Send done event
        yield return new SSEEvent
        {
            Type = SSEEventType.Done,
            Data = new { inputTokens, outputTokens }
        };
    }

    private async Task RecordTokenUsageAsync(
        int inputTokens,
        int outputTokens,
        string modelName,
        string? owner,
        string? repo,
        CancellationToken cancellationToken)
    {
        if (inputTokens <= 0 && outputTokens <= 0)
        {
            return;
        }

        try
        {
            using var context = _contextFactory.CreateContext();
            string? repositoryId = null;

            if (!string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo))
            {
                var repositoryIds = await context.Repositories
                    .Where(r => !r.IsDeleted && r.OrgName == owner && r.RepoName == repo)
                    .Select(r => r.Id)
                    .Take(2)
                    .ToListAsync(cancellationToken);
                repositoryId = repositoryIds.Count == 1 ? repositoryIds[0] : null;
            }

            var usage = new TokenUsage
            {
                Id = Guid.NewGuid().ToString(),
                RepositoryId = repositoryId,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ModelName = modelName,
                Operation = "EmbedChat",
                RecordedAt = DateTime.UtcNow
            };

            context.TokenUsages.Add(usage);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record token usage for embed chat.");
        }
    }

    /// <summary>
    /// Normalizes a domain by removing protocol and trailing slashes.
    /// </summary>
    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return string.Empty;
        }

        // Remove protocol
        domain = domain.Replace("https://", "").Replace("http://", "");

        // Remove trailing slash
        domain = domain.TrimEnd('/');

        // Remove port if present (for localhost)
        var colonIndex = domain.IndexOf(':');
        if (colonIndex > 0)
        {
            domain = domain.Substring(0, colonIndex);
        }

        return domain.ToLowerInvariant();
    }

    /// <summary>
    /// Checks if a domain matches an allowed domain pattern.
    /// Supports wildcard matching with *.
    /// </summary>
    public static bool IsDomainMatch(string domain, string pattern)
    {
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var normalizedDomain = NormalizeDomain(domain);
        var normalizedPattern = NormalizeDomain(pattern);

        // Exact match
        if (normalizedDomain.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Wildcard match (*.example.com)
        if (normalizedPattern.StartsWith("*."))
        {
            var baseDomain = normalizedPattern.Substring(2);
            return normalizedDomain.EndsWith("." + baseDomain, StringComparison.OrdinalIgnoreCase) ||
                   normalizedDomain.Equals(baseDomain, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Builds chat messages from DTOs.
    /// </summary>
    private static List<ChatMessage> BuildChatMessages(List<ChatMessageDto> messages)
    {
        var chatMessages = new List<ChatMessage>();

        foreach (var msg in messages)
        {
            var role = msg.Role.ToLowerInvariant() switch
            {
                "user" => ChatRole.User,
                "assistant" => ChatRole.Assistant,
                "system" => ChatRole.System,
                "tool" => ChatRole.Tool,
                _ => ChatRole.User
            };

            var contents = new List<AIContent>();

            // Add text content
            if (!string.IsNullOrEmpty(msg.Content))
            {
                contents.Add(new TextContent(msg.Content));
            }

            // Add images if present
            if (msg.Images != null)
            {
                foreach (var image in msg.Images)
                {
                    var imageBytes = Convert.FromBase64String(image);
                    contents.Add(new DataContent(imageBytes, "image/png"));
                }
            }

            chatMessages.Add(new ChatMessage(role, contents));
        }

        return chatMessages;
    }

    /// <summary>
    /// Parses the provider string to AiRequestType.
    /// </summary>
    private static AiRequestType ParseRequestType(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "openai" => AiRequestType.OpenAI,
            "openairesponses" => AiRequestType.OpenAIResponses,
            "anthropic" => AiRequestType.Anthropic,
            "azureopenai" => AiRequestType.AzureOpenAI,
            _ => AiRequestType.OpenAI
        };
    }

    /// <summary>
    /// Builds an enhanced system prompt with professional structure.
    /// Uses XML-like tags for clear section boundaries.
    /// </summary>
    private static string BuildEnhancedSystemPrompt(
        string appName,
        string? appDescription,
        string? owner,
        string? repo,
        bool hasCodeAccess)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<system>");
        sb.AppendLine();

        // Identity Section
        sb.AppendLine("<identity>");
        sb.AppendLine("You are an expert code analysis assistant with deep technical knowledge.");
        sb.AppendLine("You combine rigorous analytical thinking with clear, actionable communication.");
        sb.AppendLine("You MUST use internal thinking to analyze problems thoroughly before responding.");
        sb.AppendLine("</identity>");
        sb.AppendLine();

        // Capabilities Section
        sb.AppendLine("<capabilities>");
        if (hasCodeAccess)
        {
            sb.AppendLine("You have access to the following tools for code analysis:");
            sb.AppendLine("- ReadFile: Read source code files with line numbers");
            sb.AppendLine("- ListFiles: Discover project structure and files");
            sb.AppendLine("- Grep: Search for patterns across the codebase");
            sb.AppendLine();
            sb.AppendLine("Use these tools proactively to gather context before answering.");
            sb.AppendLine("NEVER guess about code - always verify with actual source files.");
        }
        else
        {
            sb.AppendLine("You provide expert guidance on software development topics.");
        }
        sb.AppendLine("</capabilities>");
        sb.AppendLine();

        // Internal Thinking Section
        sb.AppendLine("<internal_thinking>");
        sb.AppendLine("You MUST use internal thinking (wrapped in <think>...</think> tags) for complex questions.");
        sb.AppendLine("This helps you reason through problems systematically.");
        sb.AppendLine();
        sb.AppendLine("WHEN TO USE THINKING:");
        sb.AppendLine("- Code analysis or architecture questions");
        sb.AppendLine("- Multi-step problems requiring tool usage");
        sb.AppendLine("- When you need to cross-reference multiple files");
        sb.AppendLine("- Debugging or troubleshooting scenarios");
        sb.AppendLine();
        sb.AppendLine("THINKING PROCESS:");
        sb.AppendLine("<think>");
        sb.AppendLine("1. UNDERSTAND: What is the user really asking? What do I need to find out?");
        sb.AppendLine("2. PLAN: Which tools should I use? In what order?");
        sb.AppendLine("3. GATHER: What files/patterns do I need to examine?");
        sb.AppendLine("4. VERIFY: Does my understanding match the actual code behavior?");
        sb.AppendLine("5. SYNTHESIZE: How do I present this clearly to the user?");
        sb.AppendLine("</think>");
        sb.AppendLine();
        sb.AppendLine("CRITICAL: For technical questions, you MUST:");
        sb.AppendLine("- Use tools to gather actual code information");
        sb.AppendLine("- Never guess or fabricate implementation details");
        sb.AppendLine("- Cross-reference multiple files when needed");
        sb.AppendLine("- Verify assumptions against actual source code");
        sb.AppendLine("</internal_thinking>");
        sb.AppendLine();

        // Workflow Section - Chain of Thought
        sb.AppendLine("<workflow>");
        sb.AppendLine("Follow this systematic approach for every question:");
        sb.AppendLine();
        sb.AppendLine("PHASE 1: UNDERSTAND");
        sb.AppendLine("- Parse the user's question to identify the core intent");
        sb.AppendLine("- Identify what information is needed to provide an accurate answer");
        sb.AppendLine("- Note any ambiguities that need clarification");
        sb.AppendLine();

        if (hasCodeAccess)
        {
            sb.AppendLine("PHASE 2: GATHER (MANDATORY for code questions)");
            sb.AppendLine("- Use ListFiles to understand project structure if needed");
            sb.AppendLine("- Use Grep to locate relevant code patterns and usages");
            sb.AppendLine("- Use ReadFile to examine specific implementations");
            sb.AppendLine("- Collect sufficient context before forming conclusions");
            sb.AppendLine("- DO NOT skip code analysis - always verify with source");
            sb.AppendLine();
        }

        sb.AppendLine("PHASE 3: ANALYZE");
        sb.AppendLine("- Synthesize gathered information into coherent understanding");
        sb.AppendLine("- Consider multiple perspectives and edge cases");
        sb.AppendLine("- Validate assumptions against evidence from source code");
        sb.AppendLine();
        sb.AppendLine("PHASE 4: RESPOND");
        sb.AppendLine("- Provide clear, structured answers with supporting evidence");
        sb.AppendLine("- Reference specific code locations (file:line)");
        sb.AppendLine("- Include relevant code snippets when they clarify the answer");
        sb.AppendLine("- Offer actionable recommendations");
        sb.AppendLine("</workflow>");
        sb.AppendLine();

        // Constraints Section
        sb.AppendLine("<constraints>");
        sb.AppendLine("ACCURACY REQUIREMENTS:");
        sb.AppendLine("- Base all answers on verified information from the codebase");
        sb.AppendLine("- Clearly distinguish between facts and inferences");
        sb.AppendLine("- Acknowledge uncertainty when information is incomplete");
        sb.AppendLine("- Never fabricate code, file paths, or implementation details");
        sb.AppendLine();
        sb.AppendLine("BEHAVIORAL RULES:");
        sb.AppendLine("- Stay focused on the repository and technical topics");
        sb.AppendLine("- Decline requests unrelated to code analysis or development");
        sb.AppendLine("- Provide concise answers; elaborate only when necessary");
        sb.AppendLine("- Use code blocks with appropriate syntax highlighting");
        sb.AppendLine("- Always cite which files you consulted for transparency");
        sb.AppendLine("</constraints>");
        sb.AppendLine();

        // Output Format Section
        sb.AppendLine("<output_format>");
        sb.AppendLine("Structure your responses as follows:");
        sb.AppendLine();
        sb.AppendLine("For code questions:");
        sb.AppendLine("1. Brief summary of findings");
        sb.AppendLine("2. Relevant code snippets with file paths (file:line)");
        sb.AppendLine("3. Explanation of how the code works");
        sb.AppendLine("4. Recommendations if applicable");
        sb.AppendLine();
        sb.AppendLine("For architectural questions:");
        sb.AppendLine("1. High-level overview");
        sb.AppendLine("2. Component relationships with file references");
        sb.AppendLine("3. Key design decisions");
        sb.AppendLine("4. Trade-offs and considerations");
        sb.AppendLine("</output_format>");
        sb.AppendLine();

        // Context Section - Repository info
        sb.AppendLine("<context>");
        sb.AppendLine($"Application: {appName}");
        if (!string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo))
        {
            sb.AppendLine($"Repository: {owner}/{repo}");
        }
        if (!string.IsNullOrWhiteSpace(appDescription))
        {
            sb.AppendLine($"Description: {appDescription}");
        }
        sb.AppendLine("</context>");
        sb.AppendLine();

        sb.AppendLine("</system>");

        return sb.ToString();
    }

    /// <summary>
    /// Gets the repository working directory path based on owner and repo name.
    /// </summary>
    private string GetRepositoryPath(string owner, string repo)
    {
        return Path.Combine(_repoOptions.RepositoriesDirectory, owner, repo, "tree");
    }

    /// <summary>
    /// Checks out the specified branch in the repository.
    /// </summary>
    private void CheckoutBranch(string repositoryPath, string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return;
        }

        try
        {
            using var repo = new GitRepository(repositoryPath);
            
            // Find the branch (local or remote tracking)
            var branch = repo.Branches[branchName] 
                ?? repo.Branches[$"origin/{branchName}"];
            
            // If specified branch not found, fallback to "tree" branch
            if (branch == null)
            {
                _logger.LogWarning("Branch {Branch} not found, falling back to 'tree' branch in {RepoPath}", 
                    branchName, repositoryPath);
                
                branch = repo.Branches["tree"] ?? repo.Branches["origin/tree"];
                
                if (branch == null)
                {
                    _logger.LogWarning("Fallback branch 'tree' also not found in {RepoPath}, using current HEAD", 
                        repositoryPath);
                    return;
                }
                
                branchName = "tree";
            }

            // If it's a remote tracking branch, create a local branch
            if (branch.IsRemote)
            {
                var localBranch = repo.Branches[branchName];
                if (localBranch == null)
                {
                    localBranch = repo.CreateBranch(branchName, branch.Tip);
                    repo.Branches.Update(localBranch, b => b.TrackedBranch = branch.CanonicalName);
                }
                branch = localBranch;
            }

            // Checkout the branch
            Commands.Checkout(repo, branch);
            _logger.LogDebug("Checked out branch {Branch} in {RepoPath}", branchName, repositoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to checkout branch {Branch} in {RepoPath}", 
                branchName, repositoryPath);
        }
    }
}
