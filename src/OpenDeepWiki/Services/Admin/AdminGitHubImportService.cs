using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.GitHub;

namespace OpenDeepWiki.Services.Admin;

public class AdminGitHubImportService : IAdminGitHubImportService
{
    private readonly IContext _context;
    private readonly IGitHubAppService _gitHubAppService;
    private readonly IAdminSettingsService _settingsService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GitHubAppCredentialCache _cache;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminGitHubImportService> _logger;

    public AdminGitHubImportService(
        IContext context,
        IGitHubAppService gitHubAppService,
        IAdminSettingsService settingsService,
        IHttpClientFactory httpClientFactory,
        GitHubAppCredentialCache cache,
        IConfiguration configuration,
        ILogger<AdminGitHubImportService> logger)
    {
        _context = context;
        _gitHubAppService = gitHubAppService;
        _settingsService = settingsService;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GitHubStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = new GitHubStatusResponse
        {
            Configured = _gitHubAppService.IsConfigured
        };

        if (!response.Configured)
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

    public async Task<GitHubInstallationDto> StoreInstallationAsync(long installationId, CancellationToken cancellationToken = default)
    {
        // Check if already stored (including soft-deleted, to avoid UNIQUE constraint violation)
        var existing = await _context.GitHubAppInstallations
            .FirstOrDefaultAsync(i => i.InstallationId == installationId, cancellationToken);

        if (existing != null)
        {
            // Re-activate if soft-deleted
            if (existing.IsDeleted)
            {
                existing.IsDeleted = false;
                existing.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Re-activated GitHub App installation {InstallationId} for {AccountLogin}",
                    installationId, existing.AccountLogin);
            }

            return new GitHubInstallationDto
            {
                Id = existing.Id,
                InstallationId = existing.InstallationId,
                AccountLogin = existing.AccountLogin,
                AccountType = existing.AccountType,
                AccountId = existing.AccountId,
                AvatarUrl = existing.AvatarUrl,
                DepartmentId = existing.DepartmentId,
                CreatedAt = existing.CreatedAt
            };
        }

        // Fetch installation details from GitHub
        var allInstallations = await _gitHubAppService.ListInstallationsAsync(cancellationToken);
        var info = allInstallations.FirstOrDefault(i => i.Id == installationId);

        if (info == null)
            throw new InvalidOperationException($"Installation {installationId} not found on GitHub. Ensure the app is installed.");

        var entity = new GitHubAppInstallation
        {
            Id = Guid.NewGuid().ToString(),
            InstallationId = installationId,
            AccountLogin = info.AccountLogin,
            AccountType = info.AccountType,
            AccountId = info.AccountId,
            AvatarUrl = info.AvatarUrl,
        };

        _context.GitHubAppInstallations.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Stored GitHub App installation {InstallationId} for {AccountLogin}",
            installationId, info.AccountLogin);

        return new GitHubInstallationDto
        {
            Id = entity.Id,
            InstallationId = entity.InstallationId,
            AccountLogin = entity.AccountLogin,
            AccountType = entity.AccountType,
            AccountId = entity.AccountId,
            AvatarUrl = entity.AvatarUrl,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task DisconnectInstallationAsync(string installationId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.GitHubAppInstallations
            .FirstOrDefaultAsync(i => i.Id == installationId && !i.IsDeleted, cancellationToken);

        if (entity == null)
            throw new InvalidOperationException($"Installation {installationId} not found.");

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Disconnected GitHub App installation {InstallationId} for {AccountLogin}",
            entity.InstallationId, entity.AccountLogin);
    }

    public async Task<GitHubRepoListDto> ListInstallationReposAsync(
        long installationId, int page, int perPage, CancellationToken cancellationToken = default)
    {
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

    public async Task<BatchImportResult> BatchImportAsync(
        BatchImportRequest request, string ownerUserId, CancellationToken cancellationToken = default)
    {
        var result = new BatchImportResult
        {
            TotalRequested = request.Repos.Count
        };

        // Verify department exists
        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId && !d.IsDeleted, cancellationToken);

        if (department == null)
            throw new InvalidOperationException($"Department {request.DepartmentId} not found.");

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
                OwnerUserId = ownerUserId,
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

            // Assign to department
            var assignment = new RepositoryAssignment
            {
                Id = Guid.NewGuid().ToString(),
                RepositoryId = repoEntity.Id,
                DepartmentId = request.DepartmentId,
                AssigneeUserId = ownerUserId
            };

            _context.RepositoryAssignments.Add(assignment);

            result.Imported++;
            result.ImportedRepos.Add(repo.FullName);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Batch import complete: {Imported} imported, {Skipped} skipped out of {Total} requested",
            result.Imported, result.Skipped, result.TotalRequested);

        return result;
    }

    public async Task<GitHubConfigResponse> GetGitHubConfigAsync(CancellationToken cancellationToken = default)
    {
        var dbAppId = await _settingsService.GetSettingByKeyAsync("GITHUB_APP_ID");
        var dbPrivateKey = await _settingsService.GetSettingByKeyAsync("GITHUB_APP_PRIVATE_KEY");
        var dbAppName = await _settingsService.GetSettingByKeyAsync("GITHUB_APP_NAME");

        var hasDbAppId = !string.IsNullOrWhiteSpace(dbAppId?.Value);
        var hasDbPrivateKey = !string.IsNullOrWhiteSpace(dbPrivateKey?.Value);

        if (hasDbAppId || hasDbPrivateKey)
        {
            return new GitHubConfigResponse
            {
                HasAppId = hasDbAppId,
                HasPrivateKey = hasDbPrivateKey,
                AppId = dbAppId?.Value,
                AppName = dbAppName?.Value,
                Source = "database"
            };
        }

        // Check environment variables / configuration
        var envAppId = _configuration["GitHub:App:Id"]
            ?? Environment.GetEnvironmentVariable("GitHub__App__Id")
            ?? Environment.GetEnvironmentVariable("GITHUB_APP_ID");
        var envPrivateKey = _configuration["GitHub:App:PrivateKey"]
            ?? Environment.GetEnvironmentVariable("GitHub__App__PrivateKey")
            ?? Environment.GetEnvironmentVariable("GITHUB_APP_PRIVATE_KEY");
        var envAppName = _configuration["GitHub:App:Name"]
            ?? Environment.GetEnvironmentVariable("GitHub__App__Name")
            ?? Environment.GetEnvironmentVariable("GITHUB_APP_NAME");

        if (!string.IsNullOrWhiteSpace(envAppId) || !string.IsNullOrWhiteSpace(envPrivateKey))
        {
            return new GitHubConfigResponse
            {
                HasAppId = !string.IsNullOrWhiteSpace(envAppId),
                HasPrivateKey = !string.IsNullOrWhiteSpace(envPrivateKey),
                AppId = envAppId,
                AppName = envAppName,
                Source = "environment"
            };
        }

        return new GitHubConfigResponse { Source = "none" };
    }

    public async Task<GitHubConfigResponse> SaveGitHubConfigAsync(SaveGitHubConfigRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AppId))
            throw new InvalidOperationException("App ID is required.");

        string privateKeyBase64;

        if (!string.IsNullOrWhiteSpace(request.PrivateKey))
        {
            // New private key provided -- validate and encode
            if (!request.PrivateKey.Contains("-----BEGIN"))
                throw new InvalidOperationException("Private Key must be a valid PEM file (should contain '-----BEGIN').");

            privateKeyBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(request.PrivateKey));
        }
        else
        {
            // No new private key -- use existing from DB or cache
            var existingKey = _cache.PrivateKeyBase64;
            if (string.IsNullOrWhiteSpace(existingKey))
            {
                var dbKey = await _settingsService.GetSettingByKeyAsync("GITHUB_APP_PRIVATE_KEY");
                existingKey = dbKey?.Value;
            }
            if (string.IsNullOrWhiteSpace(existingKey))
                throw new InvalidOperationException("Private Key is required (no existing key found).");

            privateKeyBase64 = existingKey;
        }

        // Validate credentials by attempting to generate JWT and call GitHub API
        await ValidateGitHubCredentialsAsync(request.AppId, privateKeyBase64, cancellationToken);

        // Upsert settings into SystemSettings with category "github"
        await UpsertSettingAsync("GITHUB_APP_ID", request.AppId, "github", "GitHub App ID");
        await UpsertSettingAsync("GITHUB_APP_PRIVATE_KEY", privateKeyBase64, "github", "GitHub App private key (base64-encoded PEM)");
        await UpsertSettingAsync("GITHUB_APP_NAME", request.AppName, "github", "GitHub App name (URL slug)");
        await _context.SaveChangesAsync(cancellationToken);

        // Update the in-memory cache so IsConfigured returns true immediately
        _cache.Update(request.AppId, privateKeyBase64, request.AppName);

        _logger.LogInformation("GitHub App credentials saved to database for App ID {AppId}", request.AppId);

        return new GitHubConfigResponse
        {
            HasAppId = true,
            HasPrivateKey = true,
            AppId = request.AppId,
            AppName = request.AppName,
            Source = "database"
        };
    }

    public async Task ResetGitHubConfigAsync(CancellationToken cancellationToken = default)
    {
        // Soft-delete all GitHub config settings from SystemSettings
        var githubSettings = await _context.SystemSettings
            .Where(s => s.Category == "github" && !s.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var setting in githubSettings)
        {
            setting.IsDeleted = true;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Clear the in-memory cache
        _cache.Clear();

        _logger.LogInformation("GitHub App configuration has been reset");
    }

    private async Task ValidateGitHubCredentialsAsync(string appId, string privateKeyBase64, CancellationToken cancellationToken)
    {
        try
        {
            var pemBytes = Convert.FromBase64String(privateKeyBase64);
            var pemContent = System.Text.Encoding.UTF8.GetString(pemBytes);

            var rsa = RSA.Create();
            rsa.ImportFromPem(pemContent.AsSpan());

            var securityKey = new RsaSecurityKey(rsa);
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(
                issuer: appId,
                claims: new[]
                {
                    new Claim("iat", new DateTimeOffset(now.AddSeconds(-60)).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
                },
                expires: now.AddMinutes(10),
                signingCredentials: credentials
            );

            var jwt = new JwtSecurityTokenHandler().WriteToken(token);

            // Call GitHub API to verify the credentials
            using var client = _httpClientFactory.CreateClient("GitHubApp");
            client.BaseAddress = new Uri("https://api.github.com");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenDeepWiki", "1.0"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            client.Timeout = TimeSpan.FromSeconds(15);

            var response = await client.GetAsync("/app", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"GitHub API returned {(int)response.StatusCode}: {body}");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to validate GitHub App credentials: {ex.Message}", ex);
        }
    }

    private async Task UpsertSettingAsync(string key, string? value, string category, string description)
    {
        // Check for any existing row (including soft-deleted) to avoid UNIQUE constraint violation
        var existing = await _context.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == key);

        if (existing != null)
        {
            existing.Value = value;
            existing.Description = description;
            existing.Category = category;
            existing.IsDeleted = false;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.NewGuid().ToString(),
                Key = key,
                Value = value,
                Description = description,
                Category = category,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
