using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Services.Organizations;

namespace OpenDeepWiki.MCP;

/// <summary>
/// Resolves authenticated MCP users from Google OAuth or internal JWT tokens
/// and determines their repository access based on department assignments.
/// </summary>
public interface IMcpUserResolver
{
    Task<McpUserInfo?> ResolveUserAsync(ClaimsPrincipal principal);
    Task<List<McpRepositoryInfo>> GetAccessibleRepositoriesAsync(string userId);
    Task<bool> CanAccessRepositoryAsync(string userId, string owner, string name);
}

public class McpUserResolver : IMcpUserResolver
{
    private readonly IContext _context;
    private readonly IOrganizationService _orgService;

    public McpUserResolver(IContext context, IOrganizationService orgService)
    {
        _context = context;
        _orgService = orgService;
    }

    public async Task<McpUserInfo?> ResolveUserAsync(ClaimsPrincipal principal)
    {
        // Extract email from claims - works for both Google tokens and internal JWT
        var email = principal.FindFirstValue(ClaimTypes.Email)
                    ?? principal.FindFirstValue("email");

        if (string.IsNullOrEmpty(email))
            return null;

        // Look up user by email
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted);

        if (user == null)
            return null;

        return new McpUserInfo
        {
            UserId = user.Id,
            Email = user.Email,
            Name = user.Name
        };
    }

    public async Task<List<McpRepositoryInfo>> GetAccessibleRepositoriesAsync(string userId)
    {
        var results = new List<McpRepositoryInfo>();

        // Get repos via department assignments
        var deptRepos = await _orgService.GetDepartmentRepositoriesAsync(userId);
        foreach (var repo in deptRepos)
        {
            results.Add(new McpRepositoryInfo
            {
                Owner = repo.OrgName,
                Name = repo.RepoName,
                GitUrl = repo.GitUrl ?? string.Empty,
                Status = repo.StatusName ?? string.Empty,
                Department = repo.DepartmentName ?? string.Empty
            });
        }

        // Also include public repos the user owns
        var ownedPublicRepos = await _context.Repositories
            .Where(r => r.OwnerUserId == userId && r.IsPublic && !r.IsDeleted)
            .Select(r => new McpRepositoryInfo
            {
                Owner = r.OrgName,
                Name = r.RepoName,
                GitUrl = r.GitUrl,
                Status = r.Status.ToString(),
                Department = "(owned)"
            })
            .ToListAsync();

        foreach (var repo in ownedPublicRepos)
        {
            if (!results.Any(r => r.Owner == repo.Owner && r.Name == repo.Name))
                results.Add(repo);
        }

        return results;
    }

    public async Task<bool> CanAccessRepositoryAsync(string userId, string owner, string name)
    {
        // Check if repo is public
        var repo = await _context.Repositories
            .FirstOrDefaultAsync(r => r.OrgName == owner && r.RepoName == name && !r.IsDeleted);

        if (repo == null)
            return false;

        if (repo.IsPublic)
            return true;

        // Check department assignment
        var deptRepos = await _orgService.GetDepartmentRepositoriesAsync(userId);
        return deptRepos.Any(r => r.OrgName == owner && r.RepoName == name);
    }
}

public class McpUserInfo
{
    public required string UserId { get; set; }
    public required string Email { get; set; }
    public required string Name { get; set; }
}

public class McpRepositoryInfo
{
    public required string Owner { get; set; }
    public required string Name { get; set; }
    public required string GitUrl { get; set; }
    public required string Status { get; set; }
    public required string Department { get; set; }
}
