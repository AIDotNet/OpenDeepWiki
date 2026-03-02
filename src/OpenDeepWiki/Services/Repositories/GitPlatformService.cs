using System.Text.Json;

namespace OpenDeepWiki.Services.Repositories;

/// <summary>
/// Git平台服务实现
/// </summary>
public class GitPlatformService(IHttpClientFactory httpClientFactory, ILogger<GitPlatformService> logger, IConfiguration configuration) : IGitPlatformService
{
    private string? GitHubToken => configuration["GitHub:Token"];
    private string? GiteeToken => configuration["Gitee:Token"];
    private string? GitLabToken => configuration["GitLab:Token"];
    public async Task<GitRepoStats?> GetRepoStatsAsync(string gitUrl)
    {
        var (platform, owner, repo) = ParseGitUrl(gitUrl);
        
        if (platform == null || owner == null || repo == null)
        {
            return null;
        }

        return platform switch
        {
            "github" => await GetGitHubStatsAsync(owner, repo),
            "gitee" => await GetGiteeStatsAsync(owner, repo),
            "gitlab" => await GetGitLabStatsAsync(owner, repo),
            _ => null
        };
    }

    public async Task<GitBranchesResult> GetBranchesAsync(string gitUrl)
    {
        var (platform, owner, repo) = ParseGitUrl(gitUrl);
        
        if (platform == null || owner == null || repo == null)
        {
            return new GitBranchesResult([], null, false);
        }

        return platform switch
        {
            "github" => await GetGitHubBranchesAsync(owner, repo),
            "gitee" => await GetGiteeBranchesAsync(owner, repo),
            "gitlab" => await GetGitLabBranchesAsync(owner, repo),
            _ => new GitBranchesResult([], null, false)
        };
    }

    private static (string? platform, string? owner, string? repo) ParseGitUrl(string gitUrl)
    {
        try
        {
            // 支持格式: https://github.com/owner/repo 或 https://github.com/owner/repo.git
            var uri = new Uri(gitUrl.TrimEnd('/'));
            var host = uri.Host.ToLowerInvariant();
            
            string? platform = host switch
            {
                "github.com" => "github",
                "gitee.com" => "gitee",
                "gitlab.com" => "gitlab",
                _ => null
            };

            if (platform == null)
            {
                return (null, null, null);
            }

            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length < 2)
            {
                return (null, null, null);
            }

            var owner = segments[0];
            var repo = segments[1].Replace(".git", "", StringComparison.OrdinalIgnoreCase);

            return (platform, owner, repo);
        }
        catch
        {
            return (null, null, null);
        }
    }

    private async Task<GitRepoStats?> GetGitHubStatsAsync(string owner, string repo)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "OpenDeepWiki");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            if (!string.IsNullOrEmpty(GitHubToken))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {GitHubToken}");
            }

            var response = await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}");

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("获取GitHub仓库信息失败: {Owner}/{Repo}, 状态码: {StatusCode}", owner, repo, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var starCount = root.GetProperty("stargazers_count").GetInt32();
            var forkCount = root.GetProperty("forks_count").GetInt32();

            return new GitRepoStats(starCount, forkCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "获取GitHub仓库统计信息异常: {Owner}/{Repo}", owner, repo);
            return null;
        }
    }

    private async Task<GitRepoStats?> GetGiteeStatsAsync(string owner, string repo)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "OpenDeepWiki");

            var url = $"https://gitee.com/api/v5/repos/{owner}/{repo}";
            if (!string.IsNullOrEmpty(GiteeToken))
            {
                url += $"?access_token={GiteeToken}";
            }

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("获取Gitee仓库信息失败: {Owner}/{Repo}, 状态码: {StatusCode}", owner, repo, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var starCount = root.GetProperty("stargazers_count").GetInt32();
            var forkCount = root.GetProperty("forks_count").GetInt32();

            return new GitRepoStats(starCount, forkCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "获取Gitee仓库统计信息异常: {Owner}/{Repo}", owner, repo);
            return null;
        }
    }

    private async Task<GitBranchesResult> GetGitHubBranchesAsync(string owner, string repo)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "OpenDeepWiki");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            if (!string.IsNullOrEmpty(GitHubToken))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {GitHubToken}");
            }

            // 先获取默认分支
            var repoResponse = await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}");
            string? defaultBranch = null;

            if (repoResponse.IsSuccessStatusCode)
            {
                var repoJson = await repoResponse.Content.ReadAsStringAsync();
                using var repoDoc = JsonDocument.Parse(repoJson);
                defaultBranch = repoDoc.RootElement.GetProperty("default_branch").GetString();
            }

            // 获取分支列表（最多100个）
            var branchesResponse = await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}/branches?per_page=100");

            if (!branchesResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("获取GitHub分支列表失败: {Owner}/{Repo}, 状态码: {StatusCode}", owner, repo, branchesResponse.StatusCode);
                return new GitBranchesResult([], defaultBranch, true);
            }

            var json = await branchesResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var branches = doc.RootElement.EnumerateArray()
                .Select(b => new GitBranchInfo(
                    b.GetProperty("name").GetString() ?? "",
                    b.GetProperty("name").GetString() == defaultBranch))
                .ToList();

            return new GitBranchesResult(branches, defaultBranch, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "获取GitHub分支列表异常: {Owner}/{Repo}", owner, repo);
            return new GitBranchesResult([], null, true);
        }
    }

    private async Task<GitBranchesResult> GetGiteeBranchesAsync(string owner, string repo)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "OpenDeepWiki");

            var tokenParam = !string.IsNullOrEmpty(GiteeToken) ? $"?access_token={GiteeToken}" : "";

            // 先获取默认分支
            var repoResponse = await client.GetAsync($"https://gitee.com/api/v5/repos/{owner}/{repo}{tokenParam}");
            string? defaultBranch = null;

            if (repoResponse.IsSuccessStatusCode)
            {
                var repoJson = await repoResponse.Content.ReadAsStringAsync();
                using var repoDoc = JsonDocument.Parse(repoJson);
                defaultBranch = repoDoc.RootElement.GetProperty("default_branch").GetString();
            }

            // 获取分支列表
            var branchesUrl = $"https://gitee.com/api/v5/repos/{owner}/{repo}/branches?per_page=100";
            if (!string.IsNullOrEmpty(GiteeToken))
            {
                branchesUrl += $"&access_token={GiteeToken}";
            }
            var branchesResponse = await client.GetAsync(branchesUrl);

            if (!branchesResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("获取Gitee分支列表失败: {Owner}/{Repo}, 状态码: {StatusCode}", owner, repo, branchesResponse.StatusCode);
                return new GitBranchesResult([], defaultBranch, true);
            }

            var json = await branchesResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var branches = doc.RootElement.EnumerateArray()
                .Select(b => new GitBranchInfo(
                    b.GetProperty("name").GetString() ?? "",
                    b.GetProperty("name").GetString() == defaultBranch))
                .ToList();

            return new GitBranchesResult(branches, defaultBranch, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "获取Gitee分支列表异常: {Owner}/{Repo}", owner, repo);
            return new GitBranchesResult([], null, true);
        }
    }

    private async Task<GitRepoStats?> GetGitLabStatsAsync(string owner, string repo)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "OpenDeepWiki");
            if (!string.IsNullOrEmpty(GitLabToken))
            {
                client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", GitLabToken);
            }

            var projectPath = Uri.EscapeDataString($"{owner}/{repo}");
            var response = await client.GetAsync($"https://gitlab.com/api/v4/projects/{projectPath}");

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("获取GitLab仓库信息失败: {Owner}/{Repo}, 状态码: {StatusCode}", owner, repo, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var starCount = root.GetProperty("star_count").GetInt32();
            var forkCount = root.GetProperty("forks_count").GetInt32();

            return new GitRepoStats(starCount, forkCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "获取GitLab仓库统计信息异常: {Owner}/{Repo}", owner, repo);
            return null;
        }
    }

    private async Task<GitBranchesResult> GetGitLabBranchesAsync(string owner, string repo)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "OpenDeepWiki");
            if (!string.IsNullOrEmpty(GitLabToken))
            {
                client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", GitLabToken);
            }

            var projectPath = Uri.EscapeDataString($"{owner}/{repo}");

            // 先获取默认分支
            var repoResponse = await client.GetAsync($"https://gitlab.com/api/v4/projects/{projectPath}");
            string? defaultBranch = null;

            if (repoResponse.IsSuccessStatusCode)
            {
                var repoJson = await repoResponse.Content.ReadAsStringAsync();
                using var repoDoc = JsonDocument.Parse(repoJson);
                defaultBranch = repoDoc.RootElement.GetProperty("default_branch").GetString();
            }

            // 获取分支列表
            var branchesResponse = await client.GetAsync($"https://gitlab.com/api/v4/projects/{projectPath}/repository/branches?per_page=100");

            if (!branchesResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("获取GitLab分支列表失败: {Owner}/{Repo}, 状态码: {StatusCode}", owner, repo, branchesResponse.StatusCode);
                return new GitBranchesResult([], defaultBranch, true);
            }

            var json = await branchesResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var branches = doc.RootElement.EnumerateArray()
                .Select(b => new GitBranchInfo(
                    b.GetProperty("name").GetString() ?? "",
                    b.GetProperty("name").GetString() == defaultBranch))
                .ToList();

            return new GitBranchesResult(branches, defaultBranch, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "获取GitLab分支列表异常: {Owner}/{Repo}", owner, repo);
            return new GitBranchesResult([], null, true);
        }
    }

    public async Task<GitRepoInfo> CheckRepoExistsAsync(string owner, string repo)
    {
        // 默认检查GitHub
        return await CheckGitHubRepoAsync(owner, repo);
    }

    private async Task<GitRepoInfo> CheckGitHubRepoAsync(string owner, string repo)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "OpenDeepWiki");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            if (!string.IsNullOrEmpty(GitHubToken))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {GitHubToken}");
            }

            var response = await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}");

            if (!response.IsSuccessStatusCode)
            {
                return new GitRepoInfo(false, null, null, null, 0, 0, null, null);
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.GetProperty("name").GetString();
            var description = root.TryGetProperty("description", out var descProp) && descProp.ValueKind != JsonValueKind.Null
                ? descProp.GetString()
                : null;
            var defaultBranch = root.GetProperty("default_branch").GetString();
            var starCount = root.GetProperty("stargazers_count").GetInt32();
            var forkCount = root.GetProperty("forks_count").GetInt32();
            var language = root.TryGetProperty("language", out var langProp) && langProp.ValueKind != JsonValueKind.Null
                ? langProp.GetString()
                : null;
            var avatarUrl = root.TryGetProperty("owner", out var ownerProp)
                ? ownerProp.GetProperty("avatar_url").GetString()
                : null;
            var isPrivate = root.TryGetProperty("private", out var privateProp) && privateProp.GetBoolean();

            return new GitRepoInfo(true, name, description, defaultBranch, starCount, forkCount, language, avatarUrl, isPrivate);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "检查GitHub仓库异常: {Owner}/{Repo}", owner, repo);
            return new GitRepoInfo(false, null, null, null, 0, 0, null, null);
        }
    }
}
