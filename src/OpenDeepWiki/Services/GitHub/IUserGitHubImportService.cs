using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Models.GitHub;

namespace OpenDeepWiki.Services.GitHub;

public interface IUserGitHubImportService
{
    Task<UserGitHubStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<GitHubRepoListDto> ListInstallationReposAsync(long installationId, int page, int perPage, CancellationToken cancellationToken = default);
    Task<BatchImportResult> ImportAsync(UserImportRequest request, string userId, CancellationToken cancellationToken = default);
}
