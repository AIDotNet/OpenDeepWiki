using System.Runtime.CompilerServices;
using System.Text;
using Anthropic.Models.Messages;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Agents;
using OpenDeepWiki.Agents.Tools;
using OpenDeepWiki.Chat.Abstractions;
using OpenDeepWiki.Chat.Exceptions;
using OpenDeepWiki.Chat.Sessions;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using ChatRole = Microsoft.Extensions.AI.ChatRole;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace OpenDeepWiki.Chat.Execution;

/// <summary>
/// Agent executor for messaging providers (Slack, etc.).
/// Provides DeepWiki documentation context and tools for multi-repo queries.
/// </summary>
public class AgentExecutor : IAgentExecutor
{
    private readonly ILogger<AgentExecutor> _logger;
    private readonly AgentExecutorOptions _options;
    private readonly AgentFactory _agentFactory;
    private readonly IContextFactory _contextFactory;
    private readonly IChatUserResolver _userResolver;

    public AgentExecutor(
        ILogger<AgentExecutor> logger,
        IOptions<AgentExecutorOptions> options,
        AgentFactory agentFactory,
        IContextFactory contextFactory,
        IChatUserResolver userResolver)
    {
        _logger = logger;
        _options = options.Value;
        _agentFactory = agentFactory;
        _contextFactory = contextFactory;
        _userResolver = userResolver;
    }

    /// <inheritdoc />
    public async Task<AgentResponse> ExecuteAsync(
        IChatMessage message,
        IChatSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(session);

        _logger.LogInformation(
            "Executing agent for session {SessionId}, message {MessageId}",
            session.SessionId, message.MessageId);

        try
        {
            // Resolve platform user to DeepWiki user for permission-aware access
            var deepWikiUserId = await _userResolver.ResolveDeepWikiUserIdAsync(
                message.SenderId, message.Platform, cancellationToken);

            // Build DeepWiki tools (user-scoped for permissions)
            var docTool = await ChatMultiRepoDocTool.CreateAsync(_contextFactory, deepWikiUserId, cancellationToken);
            var tools = docTool.GetTools().ToArray();

            // Build system prompt with DeepWiki context
            var systemPrompt = BuildDeepWikiSystemPrompt();

            // Create agent with tools
            var agentOptions = new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Tools = tools,
                    ToolMode = ChatToolMode.Auto,
                    MaxOutputTokens = 32000
                }
            };

            var (agent, _) = _agentFactory.CreateChatClientWithTools(
                _options.DefaultModel,
                tools,
                agentOptions);

            // Build context messages
            var contextMessages = BuildContextMessages(message, session);
            var chatMessages = BuildAIChatMessages(contextMessages, systemPrompt);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            // Stream and collect response
            var thread = await agent.CreateSessionAsync(cts.Token);
            var contentBuilder = new StringBuilder();
            var inputTokens = 0;
            var outputTokens = 0;

            await foreach (var update in agent.RunStreamingAsync(chatMessages, thread, cancellationToken: cts.Token))
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

            var responseContent = contentBuilder.ToString();

            var responseMessage = new Abstractions.ChatMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                SenderId = "assistant",
                ReceiverId = message.SenderId,
                Content = responseContent,
                MessageType = ChatMessageType.Text,
                Platform = message.Platform,
                Timestamp = DateTimeOffset.UtcNow
            };

            var operationName = BuildOperationName(session);
            await RecordTokenUsageAsync(inputTokens, outputTokens, _options.DefaultModel, operationName, cts.Token);

            if (inputTokens > 0 || outputTokens > 0)
            {
                _logger.LogInformation(
                    "Agent execution completed for session {SessionId}. InputTokens: {InputTokens}, OutputTokens: {OutputTokens}, TotalTokens: {TotalTokens}",
                    session.SessionId,
                    inputTokens,
                    outputTokens,
                    inputTokens + outputTokens);
            }
            else
            {
                _logger.LogInformation(
                    "Agent execution completed for session {SessionId}",
                    session.SessionId);
            }

            return AgentResponse.CreateSuccess(responseMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Agent execution cancelled for session {SessionId}",
                session.SessionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Agent execution failed for session {SessionId}",
                session.SessionId);

            return CreateFriendlyErrorResponse(ex);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentResponseChunk> ExecuteStreamAsync(
        IChatMessage message,
        IChatSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(session);

        _logger.LogInformation(
            "Starting streaming agent execution for session {SessionId}, message {MessageId}",
            session.SessionId, message.MessageId);

        // Resolve platform user to DeepWiki user for permission-aware access
        var deepWikiUserId = await _userResolver.ResolveDeepWikiUserIdAsync(
            message.SenderId, message.Platform, cancellationToken);

        // Build DeepWiki tools (user-scoped for permissions)
        ChatMultiRepoDocTool? docTool = null;
        try
        {
            docTool = await ChatMultiRepoDocTool.CreateAsync(_contextFactory, deepWikiUserId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create doc tool for session {SessionId}", session.SessionId);
        }

        var tools = docTool?.GetTools().ToArray() ?? Array.Empty<AITool>();
        var systemPrompt = BuildDeepWikiSystemPrompt();

        // Create agent with tools
        var agentOptions = new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                Tools = tools,
                ToolMode = ChatToolMode.Auto,
                MaxOutputTokens = 32000
            }
        };

        var (agent, _) = _agentFactory.CreateChatClientWithTools(
            _options.DefaultModel,
            tools,
            agentOptions);

        // Build context messages
        var contextMessages = BuildContextMessages(message, session);
        var chatMessages = BuildAIChatMessages(contextMessages, systemPrompt);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        AgentSession? thread = null;
        string? initError = null;

        try
        {
            thread = await agent.CreateSessionAsync(cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create agent thread for session {SessionId}",
                session.SessionId);
            initError = CreateFriendlyErrorMessage(ex);
        }

        if (initError != null || thread == null)
        {
            yield return AgentResponseChunk.CreateError(initError ?? _options.FriendlyErrorMessage);
            yield break;
        }

        var chunks = new List<AgentResponseChunk>();
        string? streamError = null;
        var inputTokens = 0;
        var outputTokens = 0;

        try
        {
            await foreach (var update in agent.RunStreamingAsync(chatMessages, thread, cancellationToken: cts.Token))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    chunks.Add(AgentResponseChunk.CreateContent(update.Text));
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
            chunks.Add(AgentResponseChunk.CreateComplete());

            var operationName = BuildOperationName(session);
            await RecordTokenUsageAsync(inputTokens, outputTokens, _options.DefaultModel, operationName, cts.Token);

            if (inputTokens > 0 || outputTokens > 0)
            {
                _logger.LogInformation(
                    "Streaming agent execution completed for session {SessionId}. InputTokens: {InputTokens}, OutputTokens: {OutputTokens}, TotalTokens: {TotalTokens}",
                    session.SessionId,
                    inputTokens,
                    outputTokens,
                    inputTokens + outputTokens);
            }
            else
            {
                _logger.LogInformation(
                    "Streaming agent execution completed for session {SessionId}",
                    session.SessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error during streaming for session {SessionId}",
                session.SessionId);
            streamError = CreateFriendlyErrorMessage(ex);
        }

        foreach (var chunk in chunks)
        {
            yield return chunk;
        }

        if (streamError != null)
        {
            yield return AgentResponseChunk.CreateError(streamError);
        }
    }

    /// <summary>
    /// Builds DeepWiki-aware system prompt for messaging providers.
    /// </summary>
    private static string BuildDeepWikiSystemPrompt()
    {
        var sb = new StringBuilder();

        sb.AppendLine("<system>");
        sb.AppendLine();

        sb.AppendLine("<identity>");
        sb.AppendLine("You are DeepWiki, an AI documentation assistant that helps users explore and understand code repositories.");
        sb.AppendLine("You have access to indexed repository documentation and can answer questions about any repository in the system.");
        sb.AppendLine("You respond in the same language the user writes in.");
        sb.AppendLine("</identity>");
        sb.AppendLine();

        sb.AppendLine("<capabilities>");
        sb.AppendLine("You have access to these tools:");
        sb.AppendLine("- ListRepositories: List all indexed repositories available in DeepWiki");
        sb.AppendLine("- ListDocuments: List the documentation catalog for a specific repository");
        sb.AppendLine("- ReadDoc: Read documentation content from a repository (specify owner, repo, path, startLine, endLine)");
        sb.AppendLine();
        sb.AppendLine("Use these tools proactively to gather context before answering questions.");
        sb.AppendLine("</capabilities>");
        sb.AppendLine();

        sb.AppendLine("<workflow>");
        sb.AppendLine("When a user asks a question:");
        sb.AppendLine();
        sb.AppendLine("1. IDENTIFY the repository: If the user mentions a specific repo, use it directly.");
        sb.AppendLine("   If unclear, use ListRepositories to find relevant repos.");
        sb.AppendLine();
        sb.AppendLine("2. DISCOVER documentation: Use ListDocuments to see what documentation is available.");
        sb.AppendLine();
        sb.AppendLine("3. READ relevant docs: Use ReadDoc to fetch the content that answers the question.");
        sb.AppendLine("   Start with startLine=1 and a reasonable endLine (e.g., 100). If the document is");
        sb.AppendLine("   longer, read additional sections as needed.");
        sb.AppendLine();
        sb.AppendLine("4. RESPOND with accurate information based on the documentation you read.");
        sb.AppendLine("   Always cite which repository and document you found the information in.");
        sb.AppendLine("</workflow>");
        sb.AppendLine();

        sb.AppendLine("<constraints>");
        sb.AppendLine("- ALWAYS use tools to look up information. Never guess or fabricate documentation content.");
        sb.AppendLine("- If a repository is not indexed, say so clearly and suggest the user add it to DeepWiki.");
        sb.AppendLine("- Keep responses concise and focused. Use code blocks for code snippets.");
        sb.AppendLine("- If the question is not about any indexed repository, politely explain your scope.");
        sb.AppendLine("- Respond in the same language the user writes in (English, Czech, etc.).");
        sb.AppendLine("</constraints>");
        sb.AppendLine();

        sb.AppendLine("</system>");

        return sb.ToString();
    }

    /// <summary>
    /// Build context messages from session history and current message.
    /// </summary>
    private static List<IChatMessage> BuildContextMessages(IChatMessage currentMessage, IChatSession session)
    {
        var messages = new List<IChatMessage>();
        messages.AddRange(session.History);
        messages.Add(currentMessage);
        return messages;
    }

    /// <summary>
    /// Build AI chat messages with system prompt.
    /// </summary>
    private static List<AIChatMessage> BuildAIChatMessages(List<IChatMessage> contextMessages, string systemPrompt)
    {
        var chatMessages = new List<AIChatMessage>
        {
            new(ChatRole.System, systemPrompt)
        };

        foreach (var historyMsg in contextMessages)
        {
            var role = historyMsg.SenderId == "assistant"
                ? ChatRole.Assistant
                : ChatRole.User;
            chatMessages.Add(new AIChatMessage(role, historyMsg.Content));
        }

        return chatMessages;
    }

    private static string BuildOperationName(IChatSession session)
    {
        return string.IsNullOrWhiteSpace(session.Platform)
            ? "chat"
            : $"chat:{session.Platform}";
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
    /// Create a friendly error response.
    /// </summary>
    private AgentResponse CreateFriendlyErrorResponse(Exception ex)
    {
        return AgentResponse.CreateFailure(CreateFriendlyErrorMessage(ex));
    }

    /// <summary>
    /// Create a friendly error message based on exception type.
    /// </summary>
    private string CreateFriendlyErrorMessage(Exception ex)
    {
        if (ex is ChatException chatEx)
        {
            return $"{_options.FriendlyErrorMessage} (Error code: {chatEx.ErrorCode})";
        }

        if (ex is TimeoutException or OperationCanceledException)
        {
            return "The request timed out. Please try again later.";
        }

        return _options.FriendlyErrorMessage;
    }
}
