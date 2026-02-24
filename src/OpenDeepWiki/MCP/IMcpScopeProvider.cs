using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OpenDeepWiki.MCP;

public static class McpRepositoryScopeAccessor
{
    public const string ScopeKey = "repositoryScope";
    public const string OwnerKey = "owner";
    public const string RepoKey = "repo";

    public static void SetScope(McpServer mcpServer, string? owner, string? repo)
    {
        ArgumentNullException.ThrowIfNull(mcpServer);
        SetScope(mcpServer.ServerOptions, owner, repo);
    }

    public static void SetScope(McpServerOptions options, string? owner, string? repo)
    {
        ArgumentNullException.ThrowIfNull(options);

        var experimental = EnsureExperimental(options);
        if (experimental == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            ClearScope(experimental);
            return;
        }

        ApplyScope(experimental, owner, repo);
    }

    public static (string? Owner, string? Repo) GetScope(McpServer mcpServer)
    {
        ArgumentNullException.ThrowIfNull(mcpServer);
        return GetScope(mcpServer.ServerOptions);
    }

    public static (string? Owner, string? Repo) GetScope(McpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var experimental = options.Capabilities?.Experimental;
        if (experimental == null)
        {
            return (null, null);
        }

        return ExtractScope(experimental);
    }

    private static object? EnsureExperimental(McpServerOptions options)
    {
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Experimental = new Dictionary<string, object>();
        return options.Capabilities.Experimental;
    }

    private static void ApplyScope(object experimental, string owner, string repo)
    {
        if (experimental is JsonObject jsonObject)
        {
            jsonObject[ScopeKey] = new JsonObject
            {
                [OwnerKey] = owner,
                [RepoKey] = repo
            };
            return;
        }

        if (experimental is IDictionary<string, JsonNode?> jsonNodeMap)
        {
            jsonNodeMap[ScopeKey] = new JsonObject
            {
                [OwnerKey] = owner,
                [RepoKey] = repo
            };
            return;
        }

        if (experimental is IDictionary<string, object?> dict)
        {
            dict[ScopeKey] = new Dictionary<string, string>
            {
                [OwnerKey] = owner,
                [RepoKey] = repo
            };
        }
    }

    private static void ClearScope(object experimental)
    {
        if (experimental is JsonObject jsonObject)
        {
            jsonObject.Remove(ScopeKey);
            return;
        }

        if (experimental is IDictionary<string, JsonNode?> jsonNodeMap)
        {
            jsonNodeMap.Remove(ScopeKey);
            return;
        }

        if (experimental is IDictionary<string, object?> dict)
        {
            dict.Remove(ScopeKey);
        }
    }

    private static (string? Owner, string? Repo) ExtractScope(object experimental)
    {
        if (experimental is JsonObject jsonObject)
        {
            return ExtractScopeFromJsonNode(jsonObject[ScopeKey]);
        }

        if (experimental is IDictionary<string, JsonNode?> jsonNodeMap
            && jsonNodeMap.TryGetValue(ScopeKey, out var node))
        {
            return ExtractScopeFromJsonNode(node);
        }

        if (experimental is IDictionary<string, object?> dict
            && dict.TryGetValue(ScopeKey, out var value))
        {
            if (value is JsonObject innerJson)
            {
                return ExtractScopeFromJsonNode(innerJson);
            }

            if (value is IDictionary<string, object?> innerObject)
            {
                innerObject.TryGetValue(OwnerKey, out var ownerObj);
                innerObject.TryGetValue(RepoKey, out var repoObj);
                return (ownerObj as string, repoObj as string);
            }

            if (value is IDictionary<string, string> innerString)
            {
                innerString.TryGetValue(OwnerKey, out var owner);
                innerString.TryGetValue(RepoKey, out var repo);
                return (owner, repo);
            }
        }

        return (null, null);
    }

    private static (string? Owner, string? Repo) ExtractScopeFromJsonNode(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            var owner = jsonObject[OwnerKey]?.GetValue<string>();
            var repo = jsonObject[RepoKey]?.GetValue<string>();
            return (owner, repo);
        }

        return (null, null);
    }
}
