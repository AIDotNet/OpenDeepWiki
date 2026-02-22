using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Admin;
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.GitHub;

namespace OpenDeepWiki.Endpoints.Admin;

public static class AdminGitHubImportEndpoints
{
    public static RouteGroupBuilder MapAdminGitHubImportEndpoints(this RouteGroupBuilder group)
    {
        var github = group.MapGroup("/github")
            .WithTags("Admin - GitHub Import");

        github.MapGet("/status", async (
            IAdminGitHubImportService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetStatusAsync(cancellationToken);
            return Results.Ok(new { success = true, data = result });
        });

        github.MapGet("/config", async (
            IAdminGitHubImportService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetGitHubConfigAsync(cancellationToken);
            return Results.Ok(new { success = true, data = result });
        });

        github.MapPost("/config", async (
            SaveGitHubConfigRequest request,
            IAdminGitHubImportService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.SaveGitHubConfigAsync(request, cancellationToken);
                return Results.Ok(new { success = true, data = result });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        github.MapDelete("/config", async (
            IAdminGitHubImportService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await service.ResetGitHubConfigAsync(cancellationToken);
                return Results.Ok(new { success = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        github.MapGet("/install-url", async (
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            GitHubAppCredentialCache cache) =>
        {
            var appName = cache.AppName
                ?? configuration["GitHub:App:Name"]
                ?? Environment.GetEnvironmentVariable("GitHub__App__Name")
                ?? Environment.GetEnvironmentVariable("GITHUB_APP_NAME")
                ?? "deepwiki-keboola";
            var url = $"https://github.com/apps/{appName}/installations/new";
            return Results.Ok(new { success = true, data = new { url, appName } });
        });

        github.MapPost("/installations", async (
            StoreInstallationRequest request,
            IAdminGitHubImportService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.StoreInstallationAsync(request.InstallationId, cancellationToken);
                return Results.Ok(new { success = true, data = result });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        github.MapDelete("/installations/{installationId}", async (
            string installationId,
            IAdminGitHubImportService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await service.DisconnectInstallationAsync(installationId, cancellationToken);
                return Results.Ok(new { success = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        github.MapGet("/installations/{installationId:long}/repos", async (
            long installationId,
            int page,
            int perPage,
            IAdminGitHubImportService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.ListInstallationReposAsync(installationId, page, perPage, cancellationToken);
                return Results.Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        github.MapPost("/batch-import", async (
            BatchImportRequest request,
            IAdminGitHubImportService service,
            IUserContext userContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var userId = userContext.UserId;
                if (string.IsNullOrEmpty(userId))
                    return Results.Unauthorized();

                var result = await service.BatchImportAsync(request, userId, cancellationToken);
                return Results.Ok(new { success = true, data = result });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        return group;
    }
}
