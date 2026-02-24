using Microsoft.AspNetCore.Mvc;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Admin;

namespace OpenDeepWiki.Endpoints.Admin;

/// <summary>
/// 管理端 MCP 提供商端点
/// </summary>
public static class AdminMcpProviderEndpoints
{
    public static RouteGroupBuilder MapAdminMcpProviderEndpoints(this RouteGroupBuilder group)
    {
        var mcpGroup = group.MapGroup("/mcp-providers")
            .WithTags("管理端-MCP提供商");

        mcpGroup.MapGet("/", async ([FromServices] IAdminMcpProviderService service) =>
        {
            var result = await service.GetProvidersAsync();
            return Results.Ok(new { success = true, data = result });
        }).WithName("AdminGetMcpProviders");

        mcpGroup.MapPost("/", async (
            [FromBody] McpProviderRequest request,
            [FromServices] IAdminMcpProviderService service) =>
        {
            var result = await service.CreateProviderAsync(request);
            return Results.Ok(new { success = true, data = result });
        }).WithName("AdminCreateMcpProvider");

        mcpGroup.MapPut("/{id}", async (
            string id,
            [FromBody] McpProviderRequest request,
            [FromServices] IAdminMcpProviderService service) =>
        {
            var result = await service.UpdateProviderAsync(id, request);
            return result
                ? Results.Ok(new { success = true })
                : Results.NotFound(new { success = false });
        }).WithName("AdminUpdateMcpProvider");

        mcpGroup.MapDelete("/{id}", async (
            string id,
            [FromServices] IAdminMcpProviderService service) =>
        {
            var result = await service.DeleteProviderAsync(id);
            return result
                ? Results.Ok(new { success = true })
                : Results.NotFound(new { success = false });
        }).WithName("AdminDeleteMcpProvider");

        mcpGroup.MapGet("/usage-logs", async (
            [FromQuery] string? mcpProviderId,
            [FromQuery] string? userId,
            [FromQuery] string? toolName,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromServices] IAdminMcpProviderService service) =>
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var filter = new McpUsageLogFilter
            {
                McpProviderId = mcpProviderId,
                UserId = userId,
                ToolName = toolName,
                Page = page,
                PageSize = pageSize
            };
            var result = await service.GetUsageLogsAsync(filter);
            return Results.Ok(new { success = true, data = result });
        }).WithName("AdminGetMcpUsageLogs");

        return group;
    }
}
