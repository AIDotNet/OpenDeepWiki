using OpenDeepWiki.Models.Admin;

namespace OpenDeepWiki.Services.Admin;

public interface IAdminGitHubImportService
{
    Task<GitHubStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<GitHubInstallationDto> StoreInstallationAsync(long installationId, CancellationToken cancellationToken = default);
    Task<GitHubRepoListDto> ListInstallationReposAsync(long installationId, int page, int perPage, CancellationToken cancellationToken = default);
    Task<BatchImportResult> BatchImportAsync(BatchImportRequest request, string ownerUserId, CancellationToken cancellationToken = default);
}
