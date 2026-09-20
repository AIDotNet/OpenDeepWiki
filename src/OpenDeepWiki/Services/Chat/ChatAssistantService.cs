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
using OpenDeepWiki.Services.AI;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;
using GitRepository = LibGit2Sharp.Repository;

namespace OpenDeepWiki.Services.Chat;

/// <summary>
/// DTO for chat assistant configuration.
/// </summary>
public class ChatAssistantConfigDto
{
    public bool IsEnabled { get; set; }
    public List<string> EnabledModelIds { get; set; } = new();
    public List<string> EnabledMcpIds { get; set; } = new();
    public List<string> EnabledSkillIds { get; set; } = new();
    public string? DefaultModelId { get; set; }
    public bool EnableImageUpload { get; set; }
}

/// <summary>
/// DTO for model configuration.
/// </summary>
public class ModelConfigDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public bool IsEnabled { get; set; } = true; // 返回的模型都是启用的
}

/// <summary>
/// DTO for chat message.
/// </summary>
public class ChatMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string>? Images { get; set; }
    public List<ToolCallDto>? ToolCalls { get; set; }
    public ToolResultDto? ToolResult { get; set; }
    /// <summary>
    /// 引用的选中文本
    /// </summary>
    public QuotedTextDto? QuotedText { get; set; }
}

/// <summary>
/// DTO for quoted/selected text.
/// </summary>
public class QuotedTextDto
{
    /// <summary>
    /// 引用来源的标题（如文档标题）
    /// </summary>
    public string? Title { get; set; }
    /// <summary>
    /// 选中的文本内容
    /// </summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// DTO for tool call.
/// </summary>
public class ToolCallDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object>? Arguments { get; set; }
}

/// <summary>
/// DTO for tool result.
/// </summary>
public class ToolResultDto
{
    public string ToolCallId { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public bool IsError { get; set; }
}

/// <summary>
/// DTO for document context.
/// </summary>
public class DocContextDto
{
    public string Owner { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string CurrentDocPath { get; set; } = string.Empty;
    public List<CatalogItemDto> CatalogMenu { get; set; } = new();
    /// <summary>
    /// User's preferred language for AI responses (e.g., "zh-CN", "en")
    /// </summary>
    public string UserLanguage { get; set; } = "en";
}

/// <summary>
/// DTO for catalog item.
/// </summary>
public class CatalogItemDto
{
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public List<CatalogItemDto>? Children { get; set; }
}

/// <summary>
/// DTO for chat request.
/// </summary>
public class ChatRequest
{
    public List<ChatMessageDto> Messages { get; set; } = new();
    public string ModelId { get; set; } = string.Empty;
    public DocContextDto Context { get; set; } = new();
    public string? AppId { get; set; }
}

/// <summary>
/// SSE event types.
/// </summary>
public static class SSEEventType
{
    public const string Content = "content";
    public const string Thinking = "thinking";
    public const string ToolCall = "tool_call";
    public const string ToolResult = "tool_result";
    public const string Done = "done";
    public const string Error = "error";
}

/// <summary>
/// SSE event data.
/// </summary>
public class SSEEvent
{
    public string Type { get; set; } = string.Empty;
    public object? Data { get; set; }
}

/// <summary>
/// Interface for chat assistant service.
/// </summary>
public interface IChatAssistantService
{
    /// <summary>
    /// Gets the chat assistant configuration.
    /// </summary>
    Task<ChatAssistantConfigDto> GetConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of available models for the chat assistant.
    /// </summary>
    Task<List<ModelConfigDto>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams chat responses using SSE.
    /// </summary>
    IAsyncEnumerable<SSEEvent> StreamChatAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Chat assistant service implementation.
/// </summary>
public class ChatAssistantService : IChatAssistantService
{
    private readonly IContext _context;
    private readonly IContextFactory _contextFactory;
    private readonly AgentFactory _agentFactory;
    private readonly IAiProviderResolver _aiProviderResolver;
    private readonly IMcpToolConverter _mcpToolConverter;
    private readonly ISkillToolConverter _skillToolConverter;
    private readonly RepositoryAnalyzerOptions _repoOptions;
    private readonly IChatLogService _chatLogService;
    private readonly ILogger<ChatAssistantService> _logger;

    public ChatAssistantService(
        IContext context,
        IContextFactory contextFactory,
        AgentFactory agentFactory,
        IAiProviderResolver aiProviderResolver,
        IMcpToolConverter mcpToolConverter,
        ISkillToolConverter skillToolConverter,
        IOptions<RepositoryAnalyzerOptions> repoOptions,
        IChatLogService chatLogService,
        ILogger<ChatAssistantService> logger)
    {
        _context = context;
        _contextFactory = contextFactory;
        _agentFactory = agentFactory;
        _aiProviderResolver = aiProviderResolver;
        _mcpToolConverter = mcpToolConverter;
        _skillToolConverter = skillToolConverter;
        _repoOptions = repoOptions.Value;
        _chatLogService = chatLogService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChatAssistantConfigDto> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var config = await _context.ChatAssistantConfigs
            .FirstOrDefaultAsync(cancellationToken);

        if (config == null)
        {
            return new ChatAssistantConfigDto { IsEnabled = false };
        }

        return new ChatAssistantConfigDto
        {
            IsEnabled = config.IsEnabled,
            EnabledModelIds = ParseJsonArray(config.EnabledModelIds),
            EnabledMcpIds = ParseJsonArray(config.EnabledMcpIds),
            EnabledSkillIds = ParseJsonArray(config.EnabledSkillIds),
            DefaultModelId = config.DefaultModelId,
            EnableImageUpload = config.EnableImageUpload
        };
    }

    /// <inheritdoc />
    public async Task<List<ModelConfigDto>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(cancellationToken);

        if (!config.IsEnabled || config.EnabledModelIds.Count == 0)
        {
            return new List<ModelConfigDto>();
        }

        var requestedModelIds = config.EnabledModelIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var directSelections = new List<(string SelectionId, string ProviderId, string ModelId)>();
        var legacyModelIds = new List<string>();

        foreach (var id in requestedModelIds)
        {
            if (AiModelSelectionIds.TryParse(id, out var providerId, out var modelId))
            {
                directSelections.Add((id, providerId, modelId));
            }
            else
            {
                legacyModelIds.Add(id);
            }
        }

        var legacyModels = new Dictionary<string, ModelConfigDto>(StringComparer.OrdinalIgnoreCase);
        if (legacyModelIds.Count > 0)
        {
            var modelConfigs = await _context.ModelConfigs
                .Where(m => legacyModelIds.Contains(m.Id) && m.IsActive && !m.IsDeleted)
                .ToListAsync(cancellationToken);

            legacyModels = modelConfigs
                .Select(m => new ModelConfigDto
                {
                    Id = m.Id,
                    Name = m.Name,
                    Provider = m.Provider,
                    ModelId = m.ModelId,
                    Description = m.Description,
                    IsDefault = string.Equals(m.Id, config.DefaultModelId, StringComparison.OrdinalIgnoreCase) || m.IsDefault,
                    IsEnabled = true
                })
                .ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        }

        var directModels = await GetDirectModelDtosAsync(directSelections, config.DefaultModelId, cancellationToken);

        return requestedModelIds
            .Select(id =>
                legacyModels.TryGetValue(id, out var legacyModel)
                    ? legacyModel
                    : directModels.GetValueOrDefault(id))
            .Where(model => model != null)
            .Select(model => model!)
            .ToList();
    }

    private async Task<Dictionary<string, ModelConfigDto>> GetDirectModelDtosAsync(
        List<(string SelectionId, string ProviderId, string ModelId)> selections,
        string? defaultModelId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ModelConfigDto>(StringComparer.OrdinalIgnoreCase);
        if (selections.Count == 0)
        {
            return result;
        }

        var providerIds = selections
            .Select(selection => selection.ProviderId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var modelIds = selections
            .Select(selection => selection.ModelId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var providers = (await _context.AiProviderConfigs
                .Where(p => providerIds.Contains(p.Id) && p.IsActive && !p.IsDeleted)
                .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        var aiModels = await _context.AiModelConfigs
            .Where(m =>
                providerIds.Contains(m.ProviderId) &&
                modelIds.Contains(m.ModelId) &&
                m.IsActive &&
                !m.IsDeleted)
            .ToListAsync(cancellationToken);

        var aiModelByBinding = aiModels
            .GroupBy(m => CreateModelBindingKey(m.ProviderId, m.ModelId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var selection in selections)
        {
            if (!providers.TryGetValue(selection.ProviderId, out var provider) ||
                !aiModelByBinding.TryGetValue(CreateModelBindingKey(selection.ProviderId, selection.ModelId), out var aiModel))
            {
                continue;
            }

            result[selection.SelectionId] = new ModelConfigDto
            {
                Id = selection.SelectionId,
                Name = $"{provider.DisplayName ?? provider.Name} / {aiModel.DisplayName ?? aiModel.Name}",
                Provider = AiProviderResolver.ResolveEffectiveProviderType(
                    provider.ProviderType,
                    aiModel.ProviderType),
                ModelId = aiModel.ModelId,
                Description = aiModel.Description,
                IsDefault = string.Equals(selection.SelectionId, defaultModelId, StringComparison.OrdinalIgnoreCase) || aiModel.IsDefault,
                IsEnabled = true
            };
        }

        return result;
    }

    private static string CreateModelBindingKey(string providerId, string modelId)
    {
        return $"{providerId}:{modelId}";
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SSEEvent> StreamChatAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();
        IAsyncEnumerable<SSEEvent>? stream = null;
        Exception? capturedException = null;

        try
        {
            // Start streaming the chat response
            stream = InternalStreamChatAsync(request, sessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            capturedException = ex;
        }
        if (capturedException != null)
        {
            yield return new SSEEvent
            {
                // Log the error and return a structured error response to the client
                Type = SSEEventType.Error,
                Data = SSEErrorResponse.CreateNonRetryable(
                    ChatErrorCodes.INTERNAL_ERROR,
                    "An error occurred while initializing the request")
            };
            yield break;
        }
        // If stream is null for some reason, return an error event
        if (stream != null)
        {
            Exception? streamException = null;
            var enumerator = stream.WithCancellation(cancellationToken).GetAsyncEnumerator();
            while (true)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync()) break;
                }
                catch (Exception ex)
                {
                    streamException = ex;
                    break;
                }
                yield return enumerator.Current;
            }

            if (streamException != null)
            {
                await _chatLogService.LogChatErrorAsync(sessionId, streamException, request.ModelId, cancellationToken);
                _logger.LogError(streamException, "Error during streaming for session {SessionId}", sessionId);
                yield return new SSEEvent { Type = SSEEventType.Error, Data = "Stream interrupted" };
            }
        }
    }
    

    private async IAsyncEnumerable<SSEEvent> InternalStreamChatAsync(
    ChatRequest request,
    Guid sessionId,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Get configuration
        var config = await GetConfigAsync(cancellationToken);

        if (!config.IsEnabled)
        {
            yield return new SSEEvent
            {
                Type = SSEEventType.Error,
                Data = SSEErrorResponse.CreateNonRetryable(
                    ChatErrorCodes.FEATURE_DISABLED,
                    "对话助手功能未启用")
            };
            yield break;
        }


        // Validate model
        var modelConfig = await GetModelConfigAsync(request.ModelId, config, cancellationToken);
        if (modelConfig == null)
        {
            yield return new SSEEvent
            {
                Type = SSEEventType.Error,
                Data = SSEErrorResponse.CreateNonRetryable(
                    ChatErrorCodes.MODEL_UNAVAILABLE,
                    "模型不可用，请选择其他模型")
            };
            yield break;
        }
        // Build tools
        var tools = new List<AITool>();

        // Calculate repository path from Owner/Repo
        var repositoryPath = GetRepositoryPath(request.Context.Owner, request.Context.Repo, request.Context.Branch);

        // Initialize GitTool with calculated repository path
        GitTool? gitTool = null;
        if (Directory.Exists(repositoryPath))
        {
            try
            {
                // Checkout to the specified branch before initializing GitTool
                CheckoutBranch(repositoryPath, request.Context.Branch);

                gitTool = new GitTool(repositoryPath);
                tools.AddRange(gitTool.GetTools());
                _logger.LogInformation("GitTool initialized for {Owner}/{Repo}@{Branch} with path {RepoPath}",
                    request.Context.Owner, request.Context.Repo, request.Context.Branch, repositoryPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize GitTool for {Owner}/{Repo}@{Branch}",
                    request.Context.Owner, request.Context.Repo, request.Context.Branch);
            }
        }
        else
        {
            _logger.LogWarning("Repository path does not exist: {RepoPath}", repositoryPath);
        }

        // Add ChatDocReaderTool with dynamic catalog
        var chatDocReaderTool = await ChatDocReaderTool.CreateAsync(
            _context,
            request.Context.Owner,
            request.Context.Repo,
            request.Context.Branch,
            request.Context.Language,
            cancellationToken);
        tools.Add(chatDocReaderTool.GetTool());

        // Add MCP tools
        if (config.EnabledMcpIds.Count > 0)
        {
            var mcpTools = await _mcpToolConverter.ConvertMcpConfigsToToolsAsync(
                config.EnabledMcpIds, cancellationToken);
            tools.AddRange(mcpTools);
        }

        // Add Skill tools
        if (config.EnabledSkillIds.Count > 0)
        {
            var skillTools = await _skillToolConverter.ConvertSkillConfigsToToolsAsync(
                config.EnabledSkillIds, cancellationToken);
            tools.AddRange(skillTools);
        }


        // Build system prompt
        var systemPrompt = BuildSystemPrompt(request.Context, gitTool != null);

        // Create agent
        var agentOptions = new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                Tools = tools.ToArray(),
                ToolMode = ChatToolMode.Auto,
                Instructions = systemPrompt,
                MaxOutputTokens = 32000
            }
        };

        var resolvedModel = await _aiProviderResolver.ResolveModelConfigAsync(modelConfig, cancellationToken);
        var requestOptions = resolvedModel.ToRequestOptions();
        using var aiScope = AiExecutionScope.Begin(_logger, new AiExecutionContext
        {
            BusinessTag = "chat_assistant",
            Description = "仓库聊天助手",
            Repository = BuildRepositoryLabel(request.Context.Owner, request.Context.Repo),
            Branch = request.Context.Branch,
            Language = request.Context.Language,
            AppId = request.AppId,
            SessionId = sessionId.ToString("N"),
            ModelId = resolvedModel.ModelId
        });

        var (agent, _) = _agentFactory.CreateChatClientWithTools(
            resolvedModel.ModelId,
            tools.ToArray(),
            agentOptions,
            requestOptions);


        // Build chat messages with system prompt
        var chatMessages = new List<ChatMessage>( );
        chatMessages.AddRange(BuildChatMessages(request.Messages));

        // Stream response
        var usageAccumulator = new AiUsageAccumulator();

        // Track tool calls with a stack to handle nested calls
        var currentBlockIndex = -1;
        var currentBlockType = "";
        var currentToolId = "";
        var currentToolName = "";
        var toolInputJson = new System.Text.StringBuilder();

        var openAiToolCalls = new Dictionary<int, (string Id, string Name, System.Text.StringBuilder Args)>();

        var thread = await agent.CreateSessionAsync(cancellationToken);

        await foreach (var update in
                          agent.RunStreamingAsync(chatMessages, thread, cancellationToken: cancellationToken))
        {
            usageAccumulator.Add(update);

            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new SSEEvent
                {
                    Type = SSEEventType.Content,
                    Data = update.Text
                };
            }
            
            // handle tool results
            if (update.RawRepresentation is OpenAI.Chat.StreamingChatCompletionUpdate chatCompletionUpdate &&
                chatCompletionUpdate.ToolCallUpdates.Count > 0)
            {
                foreach (var toolCall in chatCompletionUpdate.ToolCallUpdates)
                {
                    var index = toolCall.Index;

                    // if this is the start of a tool call, initialize tracking
                    if (!string.IsNullOrEmpty(toolCall.FunctionName))
                    {
                        var toolId = toolCall.ToolCallId ?? Guid.NewGuid().ToString();
                        openAiToolCalls[index] = (toolId, toolCall.FunctionName, new System.Text.StringBuilder());

                       // send initial tool call event with empty arguments
                        yield return new SSEEvent
                        {
                            Type = SSEEventType.ToolCall,
                            Data = new ToolCallDto
                            {
                                Id = toolId,
                                Name = toolCall.FunctionName,
                                Arguments = null
                            }
                        };
                    }

                    var str = Encoding.UTF8.GetString(toolCall.FunctionArgumentsUpdate);
                    // concatenate argument updates for this tool call
                    if (!string.IsNullOrEmpty(str) && openAiToolCalls.ContainsKey(index))
                    {
                        openAiToolCalls[index].Args.Append(str);
                    }
                }
            }

            // handle tool call completions for OpenAI streaming format (based on finish reason)
            if (update.RawRepresentation is OpenAI.Chat.StreamingChatCompletionUpdate finishUpdate)
            {
                var finishReason = finishUpdate.FinishReason;
                if (finishReason == OpenAI.Chat.ChatFinishReason.ToolCalls)
                {
                    // send tool call events for all completed tool calls
                    foreach (var kvp in openAiToolCalls)
                    {
                        var (id, name, args) = kvp.Value;
                        var argsStr = args.ToString();
                        Dictionary<string, object>? arguments = null;

                        if (!string.IsNullOrEmpty(argsStr))
                        {
                            try
                            {
                                arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsStr);
                            }
                            catch
                            {
                                // failed to parse arguments, keep as null
                            }
                        }

                        yield return new SSEEvent
                        {
                            Type = SSEEventType.ToolCall,
                            Data = new ToolCallDto
                            {
                                Id = id,
                                Name = name,
                                Arguments = arguments
                            }
                        };
                    }
                    openAiToolCalls.Clear();
                }
            }

            if (update.RawRepresentation is ChatResponseUpdate
                {
                    RawRepresentation: RawMessageStreamEvent rawMessageStreamEvent
                })
            {
                // handle custom streaming format with content block events for more granular tool call tracking
                if (rawMessageStreamEvent.Json.TryGetProperty("type", out var typeElement))
                {
                    var eventType = typeElement.GetString();

                    // handle content block start event to track new tool calls
                    if (eventType == "content_block_start")
                    {
                        // reset current block tracking
                        if (rawMessageStreamEvent.Json.TryGetProperty("index", out var indexElement))
                        {
                            currentBlockIndex = indexElement.GetInt32();
                        }

                        if (rawMessageStreamEvent.Json.TryGetProperty("content_block", out var contentBlock))
                        {
                            var blockType = contentBlock.TryGetProperty("type", out var blockTypeElement)
                                ? blockTypeElement.GetString() ?? ""
                                : "";
                            currentBlockType = blockType;

                            // handle start of thinking block
                            if (blockType == "thinking")
                            {
                                yield return new SSEEvent
                                {
                                    Type = SSEEventType.Thinking,
                                    Data = new { type = "start", index = currentBlockIndex }
                                };
                            }
                            // handle start of tool_use block
                            else if (blockType == "tool_use")
                            {
                                currentToolId = contentBlock.TryGetProperty("id", out var idElement)
                                    ? idElement.GetString() ?? ""
                                    : "";
                                currentToolName = contentBlock.TryGetProperty("name", out var nameElement)
                                    ? nameElement.GetString() ?? ""
                                    : "";
                                toolInputJson.Clear();

                                // send initial tool call event with empty arguments
                                yield return new SSEEvent
                                {
                                    Type = SSEEventType.ToolCall,
                                    Data = new ToolCallDto
                                    {
                                        Id = currentToolId,
                                        Name = currentToolName,
                                        Arguments = null
                                    }
                                };
                            }
                        }
                    }
                    // handle content block delta events to capture thinking text and tool input updates
                    else if (eventType == "content_block_delta")
                    {
                        if (rawMessageStreamEvent.Json.TryGetProperty("delta", out var delta))
                        {
                            var deltaType = delta.TryGetProperty("type", out var deltaTypeElement)
                                ? deltaTypeElement.GetString()
                                : null;

                            // handle thinking delta updates
                            if (deltaType == "thinking_delta")
                            {
                                var thinkingText = delta.TryGetProperty("thinking", out var thinkingElement)
                                    ? thinkingElement.GetString() ?? ""
                                    : "";

                                if (!string.IsNullOrEmpty(thinkingText))
                                {
                                    yield return new SSEEvent
                                    {
                                        Type = SSEEventType.Thinking,
                                        Data = new { type = "delta", content = thinkingText, index = currentBlockIndex }
                                    };
                                }
                            }
                            // handle tool input delta updates
                            else if (deltaType == "input_json_delta")
                            {
                                var partialJson = delta.TryGetProperty("partial_json", out var jsonElement)
                                    ? jsonElement.GetString() ?? ""
                                    : "";

                                // append partial JSON updates for the current tool call
                                toolInputJson.Append(partialJson);
                            }
                        }
                    }
                    else if (eventType == "content_block_stop")
                    {
                        // handle end of thinking block
                        if (currentBlockType == "tool_use" && !string.IsNullOrEmpty(currentToolId))
                        {
                            Dictionary<string, object>? arguments = null;
                            var jsonStr = toolInputJson.ToString();
                            if (!string.IsNullOrEmpty(jsonStr))
                            {
                                try
                                {
                                    arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonStr);
                                }
                                catch
                                {
                                    // failed to parse arguments, keep as null
                                }
                            }

                            // send final tool call event with complete arguments when tool_use block ends
                            yield return new SSEEvent
                            {
                                Type = SSEEventType.ToolCall,
                                Data = new ToolCallDto
                                {
                                    Id = currentToolId,
                                    Name = currentToolName,
                                    Arguments = arguments
                                }
                            };

                            // reset tool call tracking
                            currentToolId = "";
                            currentToolName = "";
                            toolInputJson.Clear();
                        }

                        currentBlockType = "";
                    }
                }
            }
        }

        var usageSnapshot = usageAccumulator.Snapshot;
        var inputTokens = usageSnapshot.InputTokens;
        var outputTokens = usageSnapshot.OutputTokens;
        var cachedInputTokens = usageSnapshot.CachedInputTokens;
        var cacheCreationInputTokens = usageSnapshot.CacheCreationInputTokens;

        // send final done event with token usage
        await RecordTokenUsageAsync(
            inputTokens,
            outputTokens,
            cachedInputTokens,
            cacheCreationInputTokens,
            resolvedModel,
            request.Context,
            cancellationToken);

        // Send done event
        yield return new SSEEvent
        {
            Type = SSEEventType.Done,
            Data = new { inputTokens, outputTokens, cachedInputTokens, cacheCreationInputTokens }
        };
    }

    private async Task RecordTokenUsageAsync(
        int inputTokens,
        int outputTokens,
        int cachedInputTokens,
        int cacheCreationInputTokens,
        ResolvedAiModel model,
        DocContextDto context,
        CancellationToken cancellationToken)
    {
        if (inputTokens <= 0 && outputTokens <= 0)
        {
            return;
        }

        try
        {
            using var dbContext = _contextFactory.CreateContext();
            var repositoryId = await TryResolveRepositoryIdAsync(dbContext, context, cancellationToken);
            var usage = new TokenUsage
            {
                Id = Guid.NewGuid().ToString(),
                RepositoryId = repositoryId,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CachedInputTokens = cachedInputTokens,
                CacheCreationInputTokens = cacheCreationInputTokens,
                ModelId = model.ModelId,
                ModelName = model.ModelName,
                Operation = "ChatAssistant",
                RecordedAt = DateTime.UtcNow
            };
            AiUsageAccounting.ApplyModelAccounting(
                usage,
                AiUsageAccounting.FromResolvedModel(model));

            dbContext.TokenUsages.Add(usage);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record token usage for chat assistant.");
        }
    }

    private static async Task<string?> TryResolveRepositoryIdAsync(
        IContext context,
        DocContextDto docContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(docContext.Owner) || string.IsNullOrWhiteSpace(docContext.Repo))
        {
            return null;
        }

        var repositoryIds = await context.Repositories
            .Where(r => !r.IsDeleted && r.OrgName == docContext.Owner && r.RepoName == docContext.Repo)
            .Select(r => r.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        return repositoryIds.Count == 1 ? repositoryIds[0] : null;
    }

    private static string? BuildRepositoryLabel(string? owner, string? repo)
    {
        return string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)
            ? null
            : $"{owner}/{repo}";
    }

    /// <summary>
    /// Gets the model configuration by ID.
    /// </summary>
    private async Task<ModelConfig?> GetModelConfigAsync(
        string modelId,
        ChatAssistantConfigDto config,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(modelId))
        {
            // Use default model
            modelId = config.DefaultModelId ?? string.Empty;
        }

        if (!config.EnabledModelIds.Contains(modelId, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (AiModelSelectionIds.TryParse(modelId, out var providerId, out var directModelId))
        {
            var provider = await _context.AiProviderConfigs
                .FirstOrDefaultAsync(p => p.Id == providerId && p.IsActive && !p.IsDeleted, cancellationToken);
            if (provider == null)
            {
                return null;
            }

            var aiModel = await _context.AiModelConfigs
                .FirstOrDefaultAsync(m =>
                    m.ProviderId == providerId &&
                    m.ModelId == directModelId &&
                    m.IsActive &&
                    !m.IsDeleted,
                    cancellationToken);
            if (aiModel == null)
            {
                return null;
            }

            return new ModelConfig
            {
                Id = modelId,
                Name = aiModel.DisplayName ?? aiModel.Name,
                Provider = AiProviderResolver.ResolveEffectiveProviderType(
                    provider.ProviderType,
                    aiModel.ProviderType),
                AiProviderId = provider.Id,
                ModelId = aiModel.ModelId,
                IsDefault = string.Equals(modelId, config.DefaultModelId, StringComparison.OrdinalIgnoreCase) || aiModel.IsDefault,
                IsActive = true,
                Description = aiModel.Description
            };
        }

        return await _context.ModelConfigs
            .FirstOrDefaultAsync(m => m.Id == modelId && m.IsActive && !m.IsDeleted, cancellationToken);
    }

    /// <summary>
    /// Builds the system prompt with document context.
    /// Uses XML-like tags for clear section boundaries.
    /// </summary>
    private static string BuildSystemPrompt(DocContextDto context, bool hasCodeAccess)
    {
        var responseLanguage = WikiLanguageNames.GetEnglishName(context.UserLanguage);

        var sb = new StringBuilder();

        sb.AppendLine("<system>");
        sb.AppendLine();

        // Identity Section
        sb.AppendLine("<identity>");
        sb.AppendLine($"You are an expert documentation and code analysis assistant for the repository \"{context.Owner}/{context.Repo}\".");
        sb.AppendLine("You combine deep technical knowledge with clear, actionable communication.");
        sb.AppendLine("You MUST use internal thinking to analyze problems thoroughly before responding.");
        sb.AppendLine("</identity>");
        sb.AppendLine();

        // Capabilities Section
        sb.AppendLine("<capabilities>");
        sb.AppendLine("You have access to the following tools:");
        sb.AppendLine("- ReadDoc: Read documentation content from the wiki catalog");
        sb.AppendLine("  IMPORTANT: ReadDoc returns a 'sourceFiles' field containing the list of source code files");
        sb.AppendLine("  that were used to generate this document. Use these paths with ReadFile for deeper analysis.");
        if (hasCodeAccess)
        {
            sb.AppendLine("- ReadFile: Read source code files with line numbers");
            sb.AppendLine("- ListFiles: Discover project structure and files");
            sb.AppendLine("- Grep: Search for patterns across the codebase");
        }
        sb.AppendLine();
        sb.AppendLine("Use these tools proactively to gather context before answering.");
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
        sb.AppendLine("- When documentation is unclear or incomplete");
        sb.AppendLine("- When you need to cross-reference multiple sources");
        sb.AppendLine();
        sb.AppendLine("THINKING PROCESS:");
        sb.AppendLine("<think>");
        sb.AppendLine("1. UNDERSTAND: What is the user really asking? What do I need to find out?");
        sb.AppendLine("2. PLAN: Which tools should I use? In what order?");
        sb.AppendLine("3. EVALUATE: Is the documentation sufficient? Do I need source code?");
        sb.AppendLine("4. VERIFY: Does my understanding match the actual code behavior?");
        sb.AppendLine("5. SYNTHESIZE: How do I present this clearly to the user?");
        sb.AppendLine("</think>");
        sb.AppendLine();
        sb.AppendLine("CRITICAL: If documentation is vague or incomplete, you MUST:");
        sb.AppendLine("- Check the 'sourceFiles' field from ReadDoc response");
        sb.AppendLine("- Use ReadFile to examine the actual source code");
        sb.AppendLine("- Use Grep to find related implementations");
        sb.AppendLine("- Never guess or fabricate information");
        sb.AppendLine("</internal_thinking>");
        sb.AppendLine();

        // Context Section
        sb.AppendLine("<context>");
        sb.AppendLine($"Repository: {context.Owner}/{context.Repo}");
        sb.AppendLine($"Branch: {context.Branch}");
        sb.AppendLine($"Document Language: {context.Language}");
        if (!string.IsNullOrEmpty(context.CurrentDocPath))
        {
            sb.AppendLine($"Current Document: {context.CurrentDocPath}");
        }
        sb.AppendLine("</context>");
        sb.AppendLine();

        // Workflow Section
        sb.AppendLine("<workflow>");
        sb.AppendLine("Follow this systematic approach for every question:");
        sb.AppendLine();
        sb.AppendLine("PHASE 1: UNDERSTAND");
        sb.AppendLine("- Parse the user's question to identify the core intent");
        sb.AppendLine("- Identify what information is needed to provide an accurate answer");
        sb.AppendLine("- Note any ambiguities that need clarification");
        sb.AppendLine();
        sb.AppendLine("PHASE 2: GATHER (MANDATORY for technical questions)");
        sb.AppendLine("- Use ReadDoc to fetch relevant documentation content");
        sb.AppendLine("- ALWAYS check the 'sourceFiles' field in ReadDoc response");
        if (hasCodeAccess)
        {
            sb.AppendLine("- If documentation is unclear or lacks detail:");
            sb.AppendLine("  * Use ReadFile on files listed in 'sourceFiles' to examine actual implementation");
            sb.AppendLine("  * Use Grep to find related code patterns and usages");
            sb.AppendLine("  * Use ListFiles to understand project structure if needed");
        }
        sb.AppendLine("- Collect sufficient context before forming conclusions");
        sb.AppendLine("- DO NOT skip code analysis when documentation is insufficient");
        sb.AppendLine();
        sb.AppendLine("PHASE 3: ANALYZE");
        sb.AppendLine("- Synthesize gathered information into coherent understanding");
        sb.AppendLine("- Cross-reference documentation with actual code behavior");
        sb.AppendLine("- Consider multiple perspectives and edge cases");
        sb.AppendLine("- Validate assumptions against evidence from source code");
        sb.AppendLine();
        sb.AppendLine("PHASE 4: RESPOND");
        sb.AppendLine("- Provide clear, structured answers with supporting evidence");
        sb.AppendLine("- Reference specific document sections AND code locations");
        sb.AppendLine("- Include relevant code snippets when they clarify the answer");
        sb.AppendLine("- Offer actionable recommendations when applicable");
        sb.AppendLine("</workflow>");
        sb.AppendLine();

        // Source Files Usage Section
        sb.AppendLine("<source_files_usage>");
        sb.AppendLine("When ReadDoc returns a 'sourceFiles' array, these are the actual source code files");
        sb.AppendLine("that were analyzed to generate the documentation. This is CRITICAL information:");
        sb.AppendLine();
        sb.AppendLine("1. If user asks about implementation details → Read the sourceFiles");
        sb.AppendLine("2. If documentation seems outdated or incomplete → Verify against sourceFiles");
        sb.AppendLine("3. If user asks 'how does X work?' → Documentation + sourceFiles together");
        sb.AppendLine("4. If user reports a discrepancy → Check sourceFiles for ground truth");
        sb.AppendLine();
        sb.AppendLine("Example workflow:");
        sb.AppendLine("- ReadDoc returns: { content: '...', sourceFiles: ['src/Service.cs', 'src/Model.cs'] }");
        sb.AppendLine("- If content doesn't fully answer the question → ReadFile('src/Service.cs', 1, 100)");
        sb.AppendLine("- Use Grep to find specific method implementations if needed");
        sb.AppendLine("</source_files_usage>");
        sb.AppendLine();

        // Constraints Section
        sb.AppendLine("<constraints>");
        sb.AppendLine("SCOPE RULES:");
        sb.AppendLine("- ONLY answer questions related to this repository's documentation, code, architecture, or usage");
        sb.AppendLine("- Politely decline unrelated questions (general knowledge, personal advice, other topics)");
        sb.AppendLine();
        sb.AppendLine("ACCURACY REQUIREMENTS:");
        sb.AppendLine("- Base all answers on actual document/code content retrieved via tools");
        sb.AppendLine("- When documentation is unclear, MUST verify with source code");
        sb.AppendLine("- Clearly distinguish between facts and inferences");
        sb.AppendLine("- Acknowledge uncertainty when information is incomplete");
        sb.AppendLine("- Never fabricate documentation content, code, or file paths");
        sb.AppendLine();
        sb.AppendLine("BEHAVIORAL RULES:");
        sb.AppendLine("- Provide concise answers; elaborate only when necessary");
        sb.AppendLine("- Use code blocks with appropriate syntax highlighting");
        sb.AppendLine("- Reference specific locations (doc paths, file:line) when citing");
        sb.AppendLine("- Always mention which sourceFiles you consulted for transparency");
        sb.AppendLine("</constraints>");
        sb.AppendLine();

        // Output Format Section
        sb.AppendLine("<output_format>");
        sb.AppendLine($"Response Language: You MUST respond in {responseLanguage}.");
        sb.AppendLine();
        sb.AppendLine("For documentation questions:");
        sb.AppendLine("1. Brief summary of findings");
        sb.AppendLine("2. Relevant content with document references");
        sb.AppendLine("3. Source code references if consulted (from sourceFiles)");
        sb.AppendLine("4. Additional context if helpful");
        sb.AppendLine();
        sb.AppendLine("For code questions:");
        sb.AppendLine("1. Brief summary of findings");
        sb.AppendLine("2. Relevant code snippets with file paths");
        sb.AppendLine("3. Explanation of how the code works");
        sb.AppendLine("4. Recommendations if applicable");
        sb.AppendLine("</output_format>");
        sb.AppendLine();

        // Refusal Example
        sb.AppendLine("<refusal_example>");
        sb.AppendLine("For unrelated questions like \"What's the weather?\" or \"Write me a poem\", respond:");
        sb.AppendLine($"\"I'm a documentation assistant for {context.Owner}/{context.Repo}. I can only help with questions about this repository's documentation, code, and usage. Is there anything about the documentation I can help you with?\"");
        sb.AppendLine("</refusal_example>");
        sb.AppendLine();

        sb.AppendLine("</system>");

        return sb.ToString();
    }

    /// <summary>
    /// Formats the catalog menu as a string.
    /// </summary>
    private static string FormatCatalogMenu(List<CatalogItemDto> items, int indent = 0)
    {
        if (items == null || items.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        var prefix = new string(' ', indent * 2);

        foreach (var item in items)
        {
            lines.Add($"{prefix}- {item.Title} ({item.Path})");
            if (item.Children != null && item.Children.Count > 0)
            {
                lines.Add(FormatCatalogMenu(item.Children, indent + 1));
            }
        }

        return string.Join("\n", lines);
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

            // Add quoted text as reference block (if present)
            if (msg.QuotedText != null && !string.IsNullOrEmpty(msg.QuotedText.Text))
            {
                var title = !string.IsNullOrEmpty(msg.QuotedText.Title)
                    ? msg.QuotedText.Title
                    : "引用内容";
                var quotedContent = $"引用来源：{title}\n<select_text>\n{msg.QuotedText.Text}\n</select_text>";
                contents.Add(new TextContent(quotedContent));
            }

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
                    // Assume base64 encoded image
                    var imageBytes = Convert.FromBase64String(image);
                    contents.Add(new DataContent(imageBytes, "image/png"));
                }
            }

            chatMessages.Add(new ChatMessage(role, contents));
        }

        return chatMessages;
    }

    /// <summary>
    /// Parses a JSON array string to a list of strings.
    /// </summary>
    private static List<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
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
    /// Gets the repository working directory path based on owner, repo name and branch.
    /// The repository is cloned to {RepositoriesDirectory}/{org}/{repo}/branches/{branch}/tree/
    /// </summary>
    private string GetRepositoryPath(string owner, string repo, string? branch)
    {
        return RepositoryWorkspacePath.Resolve(_repoOptions, owner, repo, branch);
    }

    /// <summary>
    /// Checks out the specified branch in the repository.
    /// Falls back to "tree" branch if the specified branch doesn't exist.
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
