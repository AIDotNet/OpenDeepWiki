using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using System;
using System.ClientModel;
using Anthropic;

#pragma warning disable OPENAI001

namespace OpenDeepWiki.Agents
{
    public enum AiRequestType
    {
        OpenAI,
        AzureOpenAI,
        OpenAIResponses,
        Anthropic
    }

    public class AiRequestOptions
    {
        public string? Endpoint { get; set; }
        public string? ApiKey { get; set; }
        public AiRequestType? RequestType { get; set; }
    }

    /// <summary>
    /// Options for creating an AI agent.
    /// </summary>
    public class AgentCreationOptions
    {
        /// <summary>
        /// The system instructions for the agent.
        /// </summary>
        public string? Instructions { get; set; }

        /// <summary>
        /// The tools available to the agent.
        /// </summary>
        public IEnumerable<AIFunction>? Tools { get; set; }

        /// <summary>
        /// The name of the agent.
        /// </summary>
        public string? Name { get; set; }
    }

    public class AgentFactory(IOptions<AiRequestOptions> options)
    {
        private const string DefaultEndpoint = "https://api.routin.ai/v1";
        private readonly AiRequestOptions? _options = options?.Value;

        /// <summary>
        /// 创建带拦截功能的 HttpClient
        /// </summary>
        private static HttpClient CreateHttpClient()
        {
            var handler = new LoggingHttpHandler();
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(300)
            };
        }

        public static ChatClientAgent CreateAgentInternal(
            string model,
            ChatClientAgentOptions clientAgentOptions,
            AiRequestOptions options)
        {
            var option = ResolveOptions(options, true);
            var httpClient = CreateHttpClient();

            if (option.RequestType == AiRequestType.OpenAI)
            {
                var apiKey = ResolveRequiredApiKey(option);
                var clientOptions = new OpenAIClientOptions()
                {
                    Endpoint = new Uri(option.Endpoint ?? DefaultEndpoint),
                    Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(httpClient),
                    NetworkTimeout = httpClient.Timeout
                };

                var openAiClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey),
                    clientOptions);

                var openAIClient = openAiClient.GetChatClient(model);

                return openAIClient.AsAIAgent(clientAgentOptions);
            }
            else if (option.RequestType == AiRequestType.OpenAIResponses)
            {
                var apiKey = ResolveRequiredApiKey(option);
                var clientOptions = new OpenAIClientOptions()
                {
                    Endpoint = new Uri(option.Endpoint ?? DefaultEndpoint),
                    Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(httpClient),
                    NetworkTimeout = httpClient.Timeout
                };

                var openAiClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey),
                    clientOptions);

                var openAIClient = openAiClient.GetResponsesClient(model);

                return openAIClient.AsAIAgent(clientAgentOptions);
            }
            else if (option.RequestType == AiRequestType.Anthropic)
            {
                var apiKey = ResolveRequiredApiKey(option);
                AnthropicClient client = new()
                {
                    BaseUrl = option.Endpoint ?? DefaultEndpoint,
                    ApiKey = apiKey,
                    HttpClient = httpClient,
                };

                clientAgentOptions.ChatOptions ??= new ChatOptions();
                clientAgentOptions.ChatOptions.ModelId = model;
                var anthropicClient = client.AsAIAgent(clientAgentOptions);
                return anthropicClient;
            }

            throw new NotSupportedException("Unknown AI request type.");
        }

        private static AiRequestOptions ResolveOptions(
            AiRequestOptions? options,
            bool allowEnvironmentFallback)
        {
            var resolved = new AiRequestOptions
            {
                ApiKey = options?.ApiKey,
                Endpoint = options?.Endpoint,
                RequestType = options?.RequestType
            };

            if (allowEnvironmentFallback)
            {
                if (string.IsNullOrWhiteSpace(resolved.ApiKey))
                {
                    resolved.ApiKey = Environment.GetEnvironmentVariable("CHAT_API_KEY");
                }

                if (string.IsNullOrWhiteSpace(resolved.Endpoint))
                {
                    resolved.Endpoint = Environment.GetEnvironmentVariable("ENDPOINT");
                }

                if (!resolved.RequestType.HasValue)
                {
                    resolved.RequestType = TryParseRequestType(Environment.GetEnvironmentVariable("CHAT_REQUEST_TYPE"));
                }
            }

            if (string.IsNullOrWhiteSpace(resolved.Endpoint))
            {
                resolved.Endpoint = DefaultEndpoint;
            }

            if (!resolved.RequestType.HasValue)
            {
                resolved.RequestType = AiRequestType.OpenAI;
            }

            return resolved;
        }

        private static string ResolveRequiredApiKey(AiRequestOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                return options.ApiKey!;
            }

            throw new InvalidOperationException(
                "AI API key is not configured. Configure AI:ApiKey, CHAT_API_KEY, or a request-specific API key.");
        }

        private static AiRequestType? TryParseRequestType(string? requestType)
        {
            if (string.IsNullOrWhiteSpace(requestType))
            {
                return null;
            }

            return Enum.TryParse<AiRequestType>(requestType, true, out var parsed)
                ? parsed
                : null;
        }

        /// <summary>
        /// Creates a ChatClientAgent with the specified tools.
        /// </summary>
        /// <param name="model">The model name to use.</param>
        /// <param name="tools">The AI tools to make available to the agent.</param>
        /// <param name="clientAgentOptions">Options for the chat client agent.</param>
        /// <param name="requestOptions">Optional request options override.</param>
        /// <returns>A tuple containing the ChatClientAgent and the tools list.</returns>
        public (ChatClientAgent Agent, IList<AITool> Tools) CreateChatClientWithTools(
            string model,
            AITool[] tools,
            ChatClientAgentOptions clientAgentOptions,
            AiRequestOptions? requestOptions = null)
        {
            var option = ResolveOptions(requestOptions ?? _options, true);

            // Ensure tools are set in chat options
            clientAgentOptions.ChatOptions ??= new ChatOptions();
            clientAgentOptions.ChatOptions.Tools = tools;
            clientAgentOptions.ChatOptions.ToolMode = ChatToolMode.Auto;
            var agent = CreateAgentInternal(model, clientAgentOptions, option);


            return (agent, tools);
        }

        /// <summary>
        /// Creates a simple ChatClientAgent without tools for translation tasks.
        /// </summary>
        /// <param name="model">The model name to use.</param>
        /// <param name="maxToken"></param>
        /// <param name="requestOptions">Optional request options override.</param>
        /// <returns>The ChatClientAgent.</returns>
        public ChatClientAgent CreateSimpleChatClient(
            string model,
            int maxToken = 32000,
            AiRequestOptions? requestOptions = null)
        {
            var option = ResolveOptions(requestOptions ?? _options, true);
            var clientAgentOptions = new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions()
                {
                    MaxOutputTokens = maxToken,
                },
            };

            return CreateAgentInternal(model, clientAgentOptions, option);
        }
    }
}
