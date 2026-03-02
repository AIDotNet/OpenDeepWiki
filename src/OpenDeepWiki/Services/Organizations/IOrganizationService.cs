using OpenDeepWiki.Models;

namespace OpenDeepWiki.Services.Organizations;

/// <summary>
/// Organization service interface
/// </summary>
public interface IOrganizationService
{
    Task<List<UserDepartmentInfo>> GetUserDepartmentsAsync(string userId);
    Task<List<DepartmentRepositoryInfo>> GetDepartmentRepositoriesAsync(string userId, bool includeRestricted = false);
    Task<bool> ShareRepositoryWithMyDepartmentsAsync(string userId, string repositoryId);
    Task<bool> UnshareRepositoryFromMyDepartmentsAsync(string userId, string repositoryId);
    Task<bool> RestrictRepositoryInDepartmentsAsync(string repositoryId, string adminUserId);
    Task<bool> UnrestrictRepositoryInDepartmentsAsync(string repositoryId, string adminUserId);
}

/// <summary>
/// User department information
/// </summary>
public class UserDepartmentInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsManager { get; set; }
}

/// <summary>
/// Department repository information
/// </summary>
public class DepartmentRepositoryInfo
{
    public string RepositoryId { get; set; } = string.Empty;
    public string RepoName { get; set; } = string.Empty;
    public string OrgName { get; set; } = string.Empty;
    public string? GitUrl { get; set; }
    public int Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string DepartmentId { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? PrimaryLanguage { get; set; }
    public bool IsRestricted { get; set; }
    /// <summary>
    /// Whether the repository is publicly accessible. Used by frontend to show
    /// dual icons (public + org) for repos that are both public and department-assigned.
    /// </summary>
    public bool IsPublic { get; set; }
}
