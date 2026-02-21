using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Chat;

/// <summary>
/// Interface for converting MCP configurations to AI tools.
/// </summary>
public interface IMcpToolConverter
{
    /// <summary>
    /// Converts MCP configurations to AI tools.
    /// </summary>
    /// <param name="mcpIds">List of MCP configuration IDs to convert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of AI tools created from MCP configurations.</returns>
    Task<List<AITool>> ConvertMcpConfigsToToolsAsync(
        List<string> mcpIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Converts MCP configurations to AI tools that can be used by the chat assistant.
/// </summary>
public class McpToolConverter : IMcpToolConverter
{
    private readonly IContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<McpToolConverter> _logger;

    public McpToolConverter(
        IContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<McpToolConverter> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<AITool>> ConvertMcpConfigsToToolsAsync(
        List<string> mcpIds,
        CancellationToken cancellationToken = default)
    {
        var tools = new List<AITool>();

        if (mcpIds == null || mcpIds.Count == 0)
        {
            return tools;
        }

        // Load MCP configurations from database
        var mcpConfigs = await _context.McpConfigs
            .Where(m => mcpIds.Contains(m.Id) && m.IsActive && !m.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var config in mcpConfigs)
        {
            try
            {
                var tool = CreateMcpTool(config);
                tools.Add(tool);
                _logger.LogInformation("Created MCP tool: {Name}", config.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create MCP tool for config: {Name}", config.Name);
            }
        }

        return tools;
    }

    /// <summary>
    /// Creates an AI tool from an MCP configuration.
    /// </summary>
    private AITool CreateMcpTool(McpConfig config)
    {
        // Create a wrapper function that calls the MCP server
        var callMcpAsync = async (string input, CancellationToken ct) =>
        {
            return await CallMcpServerAsync(config, input, ct);
        };

        // Create the AI function with metadata from the MCP config
        return AIFunctionFactory.Create(
            callMcpAsync,
            new AIFunctionFactoryOptions
            {
                Name = SanitizeToolName(config.Name),
                Description = config.Description ?? $"Call MCP server: {config.Name}"
            });
    }

    /// <summary>
    /// Calls the MCP server with the given input.
    /// </summary>
    private async Task<string> CallMcpServerAsync(
        McpConfig config,
        string input,
        CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            
            // Set up the request
            var request = new HttpRequestMessage(HttpMethod.Post, config.ServerUrl);
            
            // Add API key if configured
            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            }

            // Set the request body
            request.Content = JsonContent.Create(new { input });

            // Send the request
            var response = await httpClient.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("MCP call failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return JsonSerializer.Serialize(new { error = true, message = $"MCP call failed: {response.StatusCode}" });
            }

            var result = await response.Content.ReadAsStringAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling MCP server: {Name}", config.Name);
            return JsonSerializer.Serialize(new { error = true, message = $"MCP call error: {ex.Message}" });
        }
    }

    /// <summary>
    /// Sanitizes the tool name to be a valid identifier.
    /// </summary>
    private static string SanitizeToolName(string name)
    {
        // Replace spaces and special characters with underscores
        var sanitized = new string(name
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray());

        // Ensure it starts with a letter
        if (sanitized.Length > 0 && !char.IsLetter(sanitized[0]))
        {
            sanitized = "Mcp_" + sanitized;
        }

        return sanitized;
    }
}
