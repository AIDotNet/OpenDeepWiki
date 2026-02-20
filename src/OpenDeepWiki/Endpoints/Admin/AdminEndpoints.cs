namespace OpenDeepWiki.Endpoints.Admin;

/// <summary>
/// 管理端端点注册
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var adminGroup = app.MapGroup("/api/admin")
            .RequireAuthorization("AdminOnly")
            .WithTags("管理端");

        // 注册各个管理模块的端点
        adminGroup.MapAdminStatisticsEndpoints();
        adminGroup.MapAdminRepositoryEndpoints();
        adminGroup.MapAdminUserEndpoints();
        adminGroup.MapAdminRoleEndpoints();
        adminGroup.MapAdminDepartmentEndpoints();
        adminGroup.MapAdminToolsEndpoints();
        adminGroup.MapAdminSettingsEndpoints();
        adminGroup.MapAdminChatAssistantEndpoints();
        adminGroup.MapAdminGitHubImportEndpoints();

        return app;
    }
}
