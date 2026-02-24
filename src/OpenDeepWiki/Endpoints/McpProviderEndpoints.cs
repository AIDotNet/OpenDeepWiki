using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;

namespace OpenDeepWiki.Endpoints;

/// <summary>
/// 公开 MCP 提供商端点（无需鉴权）
/// </summary>
public static class McpProviderEndpoints
{
    private const string RepositoryScopedMcpPathTemplate = "/api/mcp/{owner}/{repo}";

    public static void MapMcpProviderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/mcp-providers")
            .WithTags("MCP Providers");

        // 获取所有启用的 MCP 提供商（公开，无需登录）
        group.MapGet("/", async (IContext context) =>
        {
            var providers = await context.McpProviders
                .Where(p => p.IsActive && !p.IsDeleted)
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.Name)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Description,
                    ServerUrl = RepositoryScopedMcpPathTemplate,
                    p.TransportType,
                    p.RequiresApiKey,
                    p.ApiKeyObtainUrl,
                    p.IconUrl,
                    p.MaxRequestsPerDay,
                    p.AllowedTools,
                })
                .ToListAsync();

            return Results.Ok(new { success = true, data = providers });
        }).WithName("GetPublicMcpProviders")
          .WithSummary("获取公开 MCP 提供商列表");
    }
}
