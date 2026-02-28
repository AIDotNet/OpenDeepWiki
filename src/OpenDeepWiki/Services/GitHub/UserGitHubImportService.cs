using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Models.GitHub;

namespace OpenDeepWiki.Services.GitHub;

public class UserGitHubImportService : IUserGitHubImportService
{
    private readonly IContext _context;
    private readonly IGitHubAppService _gitHubAppService;
    private readonly ILogger<UserGitHubImportService> _logger;

    public UserGitHubImportService(
        IContext context,
        IGitHubAppService gitHubAppService,
        ILogger<UserGitHubImportService> logger)
    {
        _context = context;
        _gitHubAppService = gitHubAppService;
        _logger = logger;
    }

    public async Task<UserGitHubStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = new UserGitHubStatusResponse
        {
            Available = _gitHubAppService.IsConfigured
        };

        if (!response.Available)
            return response;

        // Load stored installations from DB
        var installations = await _context.GitHubAppInstallations
            .Where(i => !i.IsDeleted)
            .ToListAsync(cancellationToken);

        // Load department names for linked installations
        var departmentIds = installations
            .Where(i => i.DepartmentId != null)
            .Select(i => i.DepartmentId!)
            .Distinct()
            .ToList();

        var departments = departmentIds.Count > 0
            ? await _context.Departments
                .Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken)
            : new Dictionary<string, string>();

        response.Installations = installations.Select(i => new GitHubInstallationDto
        {
            Id = i.Id,
            InstallationId = i.InstallationId,
            AccountLogin = i.AccountLogin,
            AccountType = i.AccountType,
            AccountId = i.AccountId,
            AvatarUrl = i.AvatarUrl,
            DepartmentId = i.DepartmentId,
            DepartmentName = i.DepartmentId != null && departments.ContainsKey(i.DepartmentId)
                ? departments[i.DepartmentId]
                : null,
            CreatedAt = i.CreatedAt
        }).ToList();

        return response;
    }

    public async Task<GitHubRepoListDto> ListInstallationReposAsync(
        long installationId, int page, int perPage, CancellationToken cancellationToken = default)
    {
        // Validate installation exists in DB
        var installationExists = await _context.GitHubAppInstallations
            .AnyAsync(i => i.InstallationId == installationId && !i.IsDeleted, cancellationToken);

        if (!installationExists)
            throw new InvalidOperationException($"Installation {installationId} not found.");

        var result = await _gitHubAppService.ListInstallationReposAsync(installationId, page, perPage, cancellationToken);

        // Check which repos are already imported
        var repoUrls = result.Repositories.Select(r => r.CloneUrl).ToList();
        var existingUrls = await _context.Repositories
            .Where(r => repoUrls.Contains(r.GitUrl) && !r.IsDeleted)
            .Select(r => r.GitUrl)
            .ToListAsync(cancellationToken);

        var existingUrlSet = new HashSet<string>(existingUrls, StringComparer.OrdinalIgnoreCase);

        return new GitHubRepoListDto
        {
            TotalCount = result.TotalCount,
            Page = page,
            PerPage = perPage,
            Repositories = result.Repositories.Select(r => new GitHubRepoDto
            {
                Id = r.Id,
                FullName = r.FullName,
                Name = r.Name,
                Owner = r.Owner,
                Private = r.Private,
                Description = r.Description,
                Language = r.Language,
                StargazersCount = r.StargazersCount,
                ForksCount = r.ForksCount,
                DefaultBranch = r.DefaultBranch,
                CloneUrl = r.CloneUrl,
                HtmlUrl = r.HtmlUrl,
                AlreadyImported = existingUrlSet.Contains(r.CloneUrl)
            }).ToList()
        };
    }

    public async Task<BatchImportResult> ImportAsync(
        UserImportRequest request, string userId, CancellationToken cancellationToken = default)
    {
        var result = new BatchImportResult
        {
            TotalRequested = request.Repos.Count
        };

        // If DepartmentId is provided, verify user belongs to that department and department exists
        if (!string.IsNullOrEmpty(request.DepartmentId))
        {
            var userBelongsToDepartment = await _context.UserDepartments
                .AnyAsync(ud => ud.UserId == userId && ud.DepartmentId == request.DepartmentId && !ud.IsDeleted, cancellationToken);

            if (!userBelongsToDepartment)
                throw new InvalidOperationException("You do not belong to the specified department.");

            var departmentExists = await _context.Departments
                .AnyAsync(d => d.Id == request.DepartmentId && !d.IsDeleted, cancellationToken);

            if (!departmentExists)
                throw new InvalidOperationException($"Department {request.DepartmentId} not found or is inactive.");
        }

        // Get existing repos by clone URL to avoid duplicates
        var requestUrls = request.Repos.Select(r => r.CloneUrl).ToList();
        var existingRepos = await _context.Repositories
            .Where(r => requestUrls.Contains(r.GitUrl) && !r.IsDeleted)
            .ToDictionaryAsync(r => r.GitUrl, r => r, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var repo in request.Repos)
        {
            if (existingRepos.ContainsKey(repo.CloneUrl))
            {
                result.Skipped++;
                result.SkippedRepos.Add(repo.FullName);
                _logger.LogDebug("Skipping {Repo}: already imported", repo.FullName);
                continue;
            }

            // Create Repository entity
            var repoEntity = new Repository
            {
                Id = Guid.NewGuid().ToString(),
                OwnerUserId = userId,
                GitUrl = repo.CloneUrl,
                RepoName = repo.Name,
                OrgName = repo.Owner,
                IsPublic = !repo.Private,
                Status = RepositoryStatus.Pending,
                PrimaryLanguage = repo.Language,
                StarCount = repo.StargazersCount,
                ForkCount = repo.ForksCount
            };

            _context.Repositories.Add(repoEntity);

            // Create default branch
            var branch = new RepositoryBranch
            {
                Id = Guid.NewGuid().ToString(),
                RepositoryId = repoEntity.Id,
                BranchName = repo.DefaultBranch
            };

            _context.RepositoryBranches.Add(branch);

            // Create branch language
            var branchLanguage = new BranchLanguage
            {
                Id = Guid.NewGuid().ToString(),
                RepositoryBranchId = branch.Id,
                LanguageCode = request.LanguageCode,
                IsDefault = true
            };

            _context.BranchLanguages.Add(branchLanguage);

            // Assign to department if provided
            if (!string.IsNullOrEmpty(request.DepartmentId))
            {
                var assignment = new RepositoryAssignment
                {
                    Id = Guid.NewGuid().ToString(),
                    RepositoryId = repoEntity.Id,
                    DepartmentId = request.DepartmentId,
                    AssigneeUserId = userId
                };

                _context.RepositoryAssignments.Add(assignment);
            }

            result.Imported++;
            result.ImportedRepos.Add(repo.FullName);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User import complete for user {UserId}: {Imported} imported, {Skipped} skipped out of {Total} requested",
            userId, result.Imported, result.Skipped, result.TotalRequested);

        return result;
    }
}
