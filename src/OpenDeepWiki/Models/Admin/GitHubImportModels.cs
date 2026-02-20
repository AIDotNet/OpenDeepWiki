namespace OpenDeepWiki.Models.Admin;

public class GitHubStatusResponse
{
    public bool Configured { get; set; }
    public string? AppName { get; set; }
    public List<GitHubInstallationDto> Installations { get; set; } = new();
}

public class GitHubInstallationDto
{
    public string? Id { get; set; }
    public long InstallationId { get; set; }
    public string AccountLogin { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public long AccountId { get; set; }
    public string? AvatarUrl { get; set; }
    public string? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class StoreInstallationRequest
{
    public long InstallationId { get; set; }
}

public class GitHubRepoDto
{
    public long Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public bool Private { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
    public int StargazersCount { get; set; }
    public int ForksCount { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public string CloneUrl { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public bool AlreadyImported { get; set; }
}

public class GitHubRepoListDto
{
    public int TotalCount { get; set; }
    public List<GitHubRepoDto> Repositories { get; set; } = new();
    public int Page { get; set; }
    public int PerPage { get; set; }
}

public class BatchImportRequest
{
    public long InstallationId { get; set; }
    public string DepartmentId { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "en";
    public List<BatchImportRepo> Repos { get; set; } = new();
}

public class BatchImportRepo
{
    public string FullName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string CloneUrl { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public bool Private { get; set; }
    public string? Language { get; set; }
    public int StargazersCount { get; set; }
    public int ForksCount { get; set; }
}

public class BatchImportResult
{
    public int TotalRequested { get; set; }
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public List<string> SkippedRepos { get; set; } = new();
    public List<string> ImportedRepos { get; set; } = new();
}
