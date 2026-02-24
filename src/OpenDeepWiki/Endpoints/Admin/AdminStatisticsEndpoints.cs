using Microsoft.AspNetCore.Mvc;
using OpenDeepWiki.Services.Admin;

namespace OpenDeepWiki.Endpoints.Admin;

/// <summary>
/// 管理端统计数据端点
/// </summary>
public static class AdminStatisticsEndpoints
{
    public static RouteGroupBuilder MapAdminStatisticsEndpoints(this RouteGroupBuilder group)
    {
        var statisticsGroup = group.MapGroup("/statistics")
            .WithTags("管理端-统计");

        // 获取仪表盘统计数据
        statisticsGroup.MapGet("/dashboard", async (
            [FromQuery] int days,
            [FromServices] IAdminStatisticsService statisticsService) =>
        {
            if (days <= 0) days = 7;
            var result = await statisticsService.GetDashboardStatisticsAsync(days);
            return Results.Ok(new { success = true, data = result });
        })
        .WithName("GetDashboardStatistics")
        .WithSummary("获取仪表盘统计数据");

        // 获取 Token 消耗统计
        statisticsGroup.MapGet("/token-usage", async (
            [FromQuery] int days,
            [FromServices] IAdminStatisticsService statisticsService) =>
        {
            if (days <= 0) days = 7;
            var result = await statisticsService.GetTokenUsageStatisticsAsync(days);
            return Results.Ok(new { success = true, data = result });
        })
        .WithName("GetTokenUsageStatistics")
        .WithSummary("获取 Token 消耗统计");

        // 获取 MCP 使用统计
        statisticsGroup.MapGet("/mcp-usage", async (
            [FromQuery] int days,
            [FromServices] IAdminMcpProviderService mcpService) =>
        {
            if (days <= 0) days = 7;
            var result = await mcpService.GetMcpUsageStatisticsAsync(days);
            return Results.Ok(new { success = true, data = result });
        })
        .WithName("GetMcpUsageStatistics")
        .WithSummary("获取 MCP 使用统计");

        return group;
    }
}
