using OpenDeepWiki.Models.Admin;

namespace OpenDeepWiki.Models.GitHub;

public class UserGitHubStatusResponse
{
    public bool Available { get; set; }
    public List<GitHubInstallationDto> Installations { get; set; } = new();
}

public class UserImportRequest
{
    public long InstallationId { get; set; }
    public string? DepartmentId { get; set; }
    public string LanguageCode { get; set; } = "en";
    public List<BatchImportRepo> Repos { get; set; } = new();
}
