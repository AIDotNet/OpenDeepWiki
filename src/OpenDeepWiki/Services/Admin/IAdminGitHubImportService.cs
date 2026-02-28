using OpenDeepWiki.Models.Admin;

namespace OpenDeepWiki.Services.Admin;

public interface IAdminGitHubImportService
{
    Task<GitHubStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<GitHubInstallationDto> StoreInstallationAsync(long installationId, CancellationToken cancellationToken = default);
    Task<GitHubRepoListDto> ListInstallationReposAsync(long installationId, int page, int perPage, CancellationToken cancellationToken = default);
    Task<BatchImportResult> BatchImportAsync(BatchImportRequest request, string ownerUserId, CancellationToken cancellationToken = default);
    Task<GitHubConfigResponse> GetGitHubConfigAsync(CancellationToken cancellationToken = default);
    Task<GitHubConfigResponse> SaveGitHubConfigAsync(SaveGitHubConfigRequest request, CancellationToken cancellationToken = default);
    Task DisconnectInstallationAsync(string installationId, CancellationToken cancellationToken = default);
    Task ResetGitHubConfigAsync(CancellationToken cancellationToken = default);
}
