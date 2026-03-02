using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OpenDeepWiki.Services.Organizations;

namespace OpenDeepWiki.Endpoints;

/// <summary>
/// 组织端点
/// </summary>
public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations")
            .RequireAuthorization()
            .WithTags("组织");

        // 获取当前用户的部门列表
        group.MapGet("/my-departments", async (
            ClaimsPrincipal user,
            [FromServices] IOrganizationService orgService) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await orgService.GetUserDepartmentsAsync(userId);
            return Results.Ok(new { success = true, data = result });
        })
        .WithName("GetMyDepartments")
        .WithSummary("获取当前用户的部门列表");

        // 获取当前用户部门下的仓库列表
        group.MapGet("/my-repositories", async (
            ClaimsPrincipal user,
            [FromServices] IOrganizationService orgService,
            [FromQuery] bool? includeRestricted) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await orgService.GetDepartmentRepositoriesAsync(userId, includeRestricted ?? false);
            return Results.Ok(new { success = true, data = result });
        })
        .WithName("GetMyDepartmentRepositories")
        .WithSummary("Get repository list for current user's departments");

        // Share a repository with current user's departments
        group.MapPost("/my-repositories/{repositoryId}/share", async (
            string repositoryId,
            ClaimsPrincipal user,
            [FromServices] IOrganizationService orgService) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await orgService.ShareRepositoryWithMyDepartmentsAsync(userId, repositoryId);
            return Results.Ok(new { success = result });
        })
        .WithName("ShareRepositoryWithMyDepartments")
        .WithSummary("Share a repository with current user's departments");

        // Unshare a repository from current user's departments
        group.MapDelete("/my-repositories/{repositoryId}/share", async (
            string repositoryId,
            ClaimsPrincipal user,
            [FromServices] IOrganizationService orgService) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var result = await orgService.UnshareRepositoryFromMyDepartmentsAsync(userId, repositoryId);
            return Results.Ok(new { success = result });
        })
        .WithName("UnshareRepositoryFromMyDepartments")
        .WithSummary("Unshare a repository from current user's departments");

        // Admin-only endpoints for repository restriction
        var adminGroup = group.MapGroup("/repositories").RequireAuthorization("AdminOnly");

        adminGroup.MapPost("/{repositoryId}/restrict", async (
            string repositoryId,
            ClaimsPrincipal user,
            [FromServices] IOrganizationService orgService) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            var result = await orgService.RestrictRepositoryInDepartmentsAsync(repositoryId, userId);
            return result ? Results.Ok(new { success = true }) : Results.BadRequest(new { success = false });
        }).WithName("RestrictRepository");

        adminGroup.MapPost("/{repositoryId}/unrestrict", async (
            string repositoryId,
            ClaimsPrincipal user,
            [FromServices] IOrganizationService orgService) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            var result = await orgService.UnrestrictRepositoryInDepartmentsAsync(repositoryId, userId);
            return result ? Results.Ok(new { success = true }) : Results.BadRequest(new { success = false });
        }).WithName("UnrestrictRepository");

        return app;
    }
}
