namespace OpenDeepWiki.Chat.Execution;

/// <summary>
/// Agent executor configuration options.
/// </summary>
public class AgentExecutorOptions
{
    /// <summary>
    /// Default model name.
    /// </summary>
    public string DefaultModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Execution timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Maximum retry count.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Default system prompt (used as fallback if DeepWiki prompt fails).
    /// </summary>
    public string DefaultSystemPrompt { get; set; } = "You are a helpful assistant.";

    /// <summary>
    /// Whether to enable streaming responses.
    /// </summary>
    public bool EnableStreaming { get; set; } = true;

    /// <summary>
    /// Friendly error message shown to users when an error occurs.
    /// </summary>
    public string FriendlyErrorMessage { get; set; } = "Sorry, an error occurred while processing your message. Please try again later.";
}
