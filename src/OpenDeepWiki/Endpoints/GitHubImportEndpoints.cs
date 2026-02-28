using OpenDeepWiki.Models.GitHub;
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.GitHub;

namespace OpenDeepWiki.Endpoints;

public static class GitHubImportEndpoints
{
    public static IEndpointRouteBuilder MapGitHubImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/github")
            .RequireAuthorization()
            .WithTags("GitHub Import");

        group.MapGet("/status", async (
            IUserGitHubImportService service,
            CancellationToken ct) =>
        {
            var result = await service.GetStatusAsync(ct);
            return Results.Ok(new { success = true, data = result });
        });

        group.MapGet("/installations/{installationId:long}/repos", async (
            long installationId,
            int page,
            int perPage,
            IUserGitHubImportService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.ListInstallationReposAsync(installationId, page, perPage, ct);
                return Results.Ok(new { success = true, data = result });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        group.MapPost("/import", async (
            UserImportRequest request,
            IUserGitHubImportService service,
            IUserContext userContext,
            CancellationToken ct) =>
        {
            var userId = userContext.UserId;
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            try
            {
                var result = await service.ImportAsync(request, userId, ct);
                return Results.Ok(new { success = true, data = result });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        return app;
    }
}
