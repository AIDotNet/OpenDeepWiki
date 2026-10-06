using System.IO.Compression;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using OpenDeepWiki.Cache.Memory;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models;
using OpenDeepWiki.Models.Admin;
using OpenDeepWiki.Services.Admin;
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.GitHub;
using OpenDeepWiki.Services.Organizations;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class RepositorySourceSubmitTests
{
    [Fact]
    public async Task SubmitLocalDirectoryAsync_ShouldRejectPathsOutsideAllowedRoots()
    {
        using var context = CreateContext();
        var allowedRoot = CreateTempDirectory();
        var forbiddenRoot = CreateTempDirectory();
        var externalRepositoryPath = Directory.CreateDirectory(Path.Combine(forbiddenRoot, "repo-outside")).FullName;

        var service = CreateService(
            context,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = CreateTempDirectory(),
                AllowedLocalPathRoots = [allowedRoot]
            });

        var request = new LocalDirectoryRepositorySubmitRequest
        {
            OrgName = "local",
            RepoName = "outside-repo",
            LocalPath = externalRepositoryPath,
            LanguageCode = "zh",
            IsPublic = false
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitLocalDirectoryAsync(request));
    }

    [Fact]
    public async Task SubmitLocalDirectoryAsync_ShouldPersistLocalSourceWithDefaultMainBranch()
    {
        using var context = CreateContext();
        var allowedRoot = CreateTempDirectory();
        var localRepositoryPath = Directory.CreateDirectory(Path.Combine(allowedRoot, "repo-local")).FullName;
        File.WriteAllText(Path.Combine(localRepositoryPath, "README.md"), "# hello");

        var service = CreateService(
            context,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = CreateTempDirectory(),
                AllowedLocalPathRoots = [allowedRoot]
            });

        var request = new LocalDirectoryRepositorySubmitRequest
        {
            OrgName = "local",
            RepoName = "repo-local",
            LocalPath = localRepositoryPath,
            LanguageCode = "zh",
            IsPublic = false
        };

        var repository = await service.SubmitLocalDirectoryAsync(request);

        Assert.Equal(RepositorySourceType.LocalDirectory, RepositorySource.Parse(repository.GitUrl).SourceType);
        Assert.Equal(localRepositoryPath, RepositorySource.Parse(repository.GitUrl).Location);

        var branch = await context.RepositoryBranches.SingleAsync(b => b.RepositoryId == repository.Id);
        var language = await context.BranchLanguages.SingleAsync(l => l.RepositoryBranchId == branch.Id);

        Assert.Equal("main", branch.BranchName);
        Assert.Equal("zh", language.LanguageCode);
        Assert.Equal(RepositoryStatus.Pending, repository.Status);
        Assert.True(repository.GenerateSkill);
    }

    [Fact]
    public async Task SubmitArchiveAsync_ShouldPersistArchiveSourceWithDefaultMainBranch()
    {
        using var context = CreateContext();
        var repositoriesRoot = CreateTempDirectory();
        var service = CreateService(
            context,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = repositoriesRoot,
                AllowedLocalPathRoots = []
            });

        using var zipStream = CreateArchiveStream(("src/app.ts", "export const app = true;"));
        IFormFile archive = new FormFile(zipStream, 0, zipStream.Length, "archive", "repo.zip");

        var request = new ArchiveRepositorySubmitRequest
        {
            OrgName = "uploads",
            RepoName = "repo-zip",
            LanguageCode = "zh",
            IsPublic = false,
            Archive = archive
        };

        var repository = await service.SubmitArchiveAsync(request);

        var descriptor = RepositorySource.Parse(repository.GitUrl);
        Assert.Equal(RepositorySourceType.Archive, descriptor.SourceType);
        Assert.True(File.Exists(descriptor.Location));

        var branch = await context.RepositoryBranches.SingleAsync(b => b.RepositoryId == repository.Id);
        Assert.Equal("main", branch.BranchName);
        Assert.True(repository.GenerateSkill);
    }

    [Fact]
    public async Task SubmitAsync_ShouldKeepGitSubmissionBackwardCompatible()
    {
        using var context = CreateContext();
        var gitPlatformService = new Mock<IGitPlatformService>();
        gitPlatformService
            .Setup(s => s.GetRepoStatsAsync("https://github.com/AIDotNet/OpenDeepWiki.git"))
            .ReturnsAsync(new GitRepoStats(10, 2));
        gitPlatformService
            .Setup(s => s.CheckRepoExistsAsync("AIDotNet", "OpenDeepWiki"))
            .ReturnsAsync(new GitRepoInfo(true, "OpenDeepWiki", null, "main", 10, 2, "C#", null, false));

        var service = CreateService(
            context,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = CreateTempDirectory(),
                AllowedLocalPathRoots = []
            },
            gitPlatformService: gitPlatformService.Object);

        var request = new RepositorySubmitRequest
        {
            GitUrl = "https://github.com/AIDotNet/OpenDeepWiki.git",
            OrgName = "AIDotNet",
            RepoName = "OpenDeepWiki",
            BranchName = "main",
            LanguageCode = "zh",
            IsPublic = true
        };

        var repository = await service.SubmitAsync(request);

        Assert.Equal(RepositorySourceType.Git, RepositorySource.Parse(repository.GitUrl).SourceType);
        Assert.Equal("https://github.com/AIDotNet/OpenDeepWiki.git", repository.GitUrl);
        Assert.Equal(10, repository.StarCount);
        Assert.Equal(2, repository.ForkCount);
        Assert.True(repository.GenerateSkill);
    }

    [Fact]
    public async Task SubmitAsync_WhenRepositoryExistsWithDifferentBranch_AddsBranchToExistingRepository()
    {
        using var context = CreateContext();
        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = "user-1",
            GitUrl = "https://github.com/YD-HW/services-youdao-input-event-monitor.git",
            OrgName = "YD_HW/services",
            RepoName = "youdao-input-event-monitor",
            Status = RepositoryStatus.Completed,
            IsPublic = false
        };
        var existingBranch = new RepositoryBranch
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchName = "smart-hw/os_services_develop",
            LastCommitId = "a-commit",
            LastProcessedAt = DateTime.UtcNow
        };
        context.Repositories.Add(repository);
        context.RepositoryBranches.Add(existingBranch);
        context.BranchLanguages.Add(new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = existingBranch.Id,
            LanguageCode = "zh",
            IsDefault = true
        });
        await context.SaveChangesAsync();
        var service = CreateService(context, new RepositoryAnalyzerOptions());

        var returnedRepository = await service.SubmitAsync(new RepositorySubmitRequest
        {
            GitUrl = repository.GitUrl,
            OrgName = repository.OrgName,
            RepoName = repository.RepoName,
            BranchName = "smart-hw/rv1106_develop",
            LanguageCode = "zh",
            IsPublic = false
        });

        Assert.Equal(repository.Id, returnedRepository.Id);
        Assert.Equal(RepositoryStatus.Completed, returnedRepository.Status);
        Assert.Equal(1, await context.Repositories.CountAsync());
        var branches = await context.RepositoryBranches
            .Where(branch => branch.RepositoryId == repository.Id)
            .OrderBy(branch => branch.BranchName)
            .ToListAsync();
        Assert.Equal(["smart-hw/os_services_develop", "smart-hw/rv1106_develop"], branches.Select(branch => branch.BranchName));
        var newBranch = branches.Single(branch => branch.BranchName == "smart-hw/rv1106_develop");
        var newLanguage = await context.BranchLanguages.SingleAsync(language => language.RepositoryBranchId == newBranch.Id);
        Assert.Equal("zh", newLanguage.LanguageCode);
        Assert.True(newLanguage.IsDefault);
    }

    [Fact]
    public async Task SubmitAsync_WhenGenerateSkillIsFalse_ShouldPersistFalse()
    {
        using var context = CreateContext();
        var gitPlatformService = new Mock<IGitPlatformService>();
        gitPlatformService
            .Setup(s => s.GetRepoStatsAsync("https://github.com/AIDotNet/OpenDeepWiki.git"))
            .ReturnsAsync(new GitRepoStats(10, 2));
        gitPlatformService
            .Setup(s => s.CheckRepoExistsAsync("AIDotNet", "OpenDeepWiki"))
            .ReturnsAsync(new GitRepoInfo(true, "OpenDeepWiki", null, "main", 10, 2, "C#", null, false));

        var service = CreateService(
            context,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = CreateTempDirectory(),
                AllowedLocalPathRoots = []
            },
            gitPlatformService: gitPlatformService.Object);

        var repository = await service.SubmitAsync(new RepositorySubmitRequest
        {
            GitUrl = "https://github.com/AIDotNet/OpenDeepWiki.git",
            OrgName = "AIDotNet",
            RepoName = "OpenDeepWiki",
            BranchName = "main",
            LanguageCode = "zh",
            IsPublic = true,
            GenerateSkill = false
        });

        Assert.False(repository.GenerateSkill);
    }

    [Fact]
    public async Task SubmitArchiveAsync_WhenGenerateSkillIsFalse_ShouldPersistFalse()
    {
        using var context = CreateContext();
        var repositoriesRoot = CreateTempDirectory();
        var service = CreateService(
            context,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = repositoriesRoot,
                AllowedLocalPathRoots = []
            });

        using var zipStream = CreateArchiveStream(("src/app.ts", "export const app = true;"));
        IFormFile archive = new FormFile(zipStream, 0, zipStream.Length, "archive", "repo.zip");

        var repository = await service.SubmitArchiveAsync(new ArchiveRepositorySubmitRequest
        {
            OrgName = "uploads",
            RepoName = "repo-zip",
            LanguageCode = "zh",
            IsPublic = false,
            GenerateSkill = false,
            Archive = archive
        });

        Assert.False(repository.GenerateSkill);
    }

    [Fact]
    public async Task SubmitLocalDirectoryAsync_WhenGenerateSkillIsFalse_ShouldPersistFalse()
    {
        using var context = CreateContext();
        var allowedRoot = CreateTempDirectory();
        var localRepositoryPath = Directory.CreateDirectory(Path.Combine(allowedRoot, "repo-local")).FullName;
        File.WriteAllText(Path.Combine(localRepositoryPath, "README.md"), "# hello");

        var service = CreateService(
            context,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = CreateTempDirectory(),
                AllowedLocalPathRoots = [allowedRoot]
            });

        var repository = await service.SubmitLocalDirectoryAsync(new LocalDirectoryRepositorySubmitRequest
        {
            OrgName = "local",
            RepoName = "repo-local",
            LocalPath = localRepositoryPath,
            LanguageCode = "zh",
            IsPublic = false,
            GenerateSkill = false
        });

        Assert.False(repository.GenerateSkill);
    }

    [Fact]
    public async Task DeleteRepositoryAsync_ShouldSoftDeleteRepositoryAndClearOptionalReferences()
    {
        using var context = CreateContext();
        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = "user-1",
            GitUrl = "https://github.com/demo/repo.git",
            OrgName = "demo",
            RepoName = "repo"
        };
        context.Repositories.Add(repository);
        context.TokenUsages.Add(new TokenUsage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            RecordedAt = DateTime.UtcNow
        });
        context.UserActivities.Add(new UserActivity
        {
            Id = Guid.NewGuid().ToString(),
            UserId = "user-1",
            RepositoryId = repository.Id,
            ActivityType = UserActivityType.View,
            Weight = 1
        });
        await context.SaveChangesAsync();

        var adminService = CreateAdminService(context);

        var deleted = await adminService.DeleteRepositoryAsync(repository.Id);

        Assert.True(deleted);
        var deletedRepository = await context.Repositories.SingleAsync(r => r.Id == repository.Id);
        Assert.True(deletedRepository.IsDeleted);
        Assert.NotNull(deletedRepository.DeletedAt);
        Assert.Null((await context.TokenUsages.SingleAsync()).RepositoryId);
        Assert.Null((await context.UserActivities.SingleAsync()).RepositoryId);
    }

    [Fact]
    public async Task DeleteRepositoryAsync_ShouldSoftDeleteRepositoryAndRemoveNestedCatalogsUsingSqliteForeignKeys()
    {
        await using var context = await CreateSqliteContextAsync();
        const string userId = "user-1";
        var repositoryId = Guid.NewGuid().ToString();
        var branchId = Guid.NewGuid().ToString();
        var languageId = Guid.NewGuid().ToString();
        var docFileId = Guid.NewGuid().ToString();
        var parentCatalogId = Guid.NewGuid().ToString();

        context.Users.Add(new User
        {
            Id = userId,
            Name = "Test User",
            Email = "test@example.com"
        });
        context.Repositories.Add(new Repository
        {
            Id = repositoryId,
            OwnerUserId = userId,
            GitUrl = "https://github.com/demo/repo.git",
            OrgName = "demo",
            RepoName = "repo"
        });
        context.RepositoryBranches.Add(new RepositoryBranch
        {
            Id = branchId,
            RepositoryId = repositoryId,
            BranchName = "main"
        });
        context.BranchLanguages.Add(new BranchLanguage
        {
            Id = languageId,
            RepositoryBranchId = branchId,
            LanguageCode = "en",
            IsDefault = true
        });
        context.DocFiles.Add(new DocFile
        {
            Id = docFileId,
            BranchLanguageId = languageId,
            Content = "Leaf content"
        });
        context.DocCatalogs.AddRange(
            new DocCatalog
            {
                Id = parentCatalogId,
                BranchLanguageId = languageId,
                Path = "1-overview",
                Title = "Overview"
            },
            new DocCatalog
            {
                Id = Guid.NewGuid().ToString(),
                BranchLanguageId = languageId,
                ParentId = parentCatalogId,
                Path = "1-overview.1-intro",
                Title = "Intro",
                DocFileId = docFileId
            });
        context.TokenUsages.Add(new TokenUsage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repositoryId,
            RecordedAt = DateTime.UtcNow
        });
        context.UserActivities.Add(new UserActivity
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            RepositoryId = repositoryId,
            ActivityType = UserActivityType.View,
            Weight = 1
        });
        await context.SaveChangesAsync();

        var adminService = CreateAdminService(context);

        var deleted = await adminService.DeleteRepositoryAsync(repositoryId);

        Assert.True(deleted);
        Assert.True((await context.Repositories.SingleAsync(r => r.Id == repositoryId)).IsDeleted);
        Assert.False(await context.RepositoryBranches.AnyAsync(b => b.RepositoryId == repositoryId));
        Assert.False(await context.BranchLanguages.AnyAsync(l => l.RepositoryBranchId == branchId));
        Assert.False(await context.DocCatalogs.AnyAsync(c => c.BranchLanguageId == languageId));
        Assert.False(await context.DocFiles.AnyAsync(f => f.BranchLanguageId == languageId));
        Assert.Null((await context.TokenUsages.SingleAsync()).RepositoryId);
        Assert.Null((await context.UserActivities.SingleAsync()).RepositoryId);
    }

    [Fact]
    public async Task SubmitAsync_ShouldRemoveSoftDeletedDuplicateBeforeRecreate()
    {
        using var context = CreateContext();
        var softDeletedRepositoryId = Guid.NewGuid().ToString();
        var branchId = Guid.NewGuid().ToString();
        context.Repositories.Add(new Repository
        {
            Id = softDeletedRepositoryId,
            OwnerUserId = "user-1",
            GitUrl = "https://github.com/AIDotNet/OpenDeepWiki.git",
            OrgName = "AIDotNet",
            RepoName = "OpenDeepWiki",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        });
        context.RepositoryBranches.Add(new RepositoryBranch
        {
            Id = branchId,
            RepositoryId = softDeletedRepositoryId,
            BranchName = "main"
        });
        context.BranchLanguages.Add(new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = branchId,
            LanguageCode = "zh",
            IsDefault = true
        });
        context.TokenUsages.Add(new TokenUsage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = softDeletedRepositoryId,
            RecordedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var gitPlatformService = new Mock<IGitPlatformService>();
        gitPlatformService
            .Setup(s => s.GetRepoStatsAsync("https://github.com/AIDotNet/OpenDeepWiki.git"))
            .ReturnsAsync(new GitRepoStats(10, 2));
        gitPlatformService
            .Setup(s => s.CheckRepoExistsAsync("AIDotNet", "OpenDeepWiki"))
            .ReturnsAsync(new GitRepoInfo(true, "OpenDeepWiki", null, "main", 10, 2, "C#", null, false));

        var service = CreateService(
            context,
            new RepositoryAnalyzerOptions
            {
                RepositoriesDirectory = CreateTempDirectory(),
                AllowedLocalPathRoots = []
            },
            gitPlatformService: gitPlatformService.Object);

        var repository = await service.SubmitAsync(new RepositorySubmitRequest
        {
            GitUrl = "https://github.com/AIDotNet/OpenDeepWiki.git",
            OrgName = "AIDotNet",
            RepoName = "OpenDeepWiki",
            BranchName = "main",
            LanguageCode = "zh",
            IsPublic = true
        });

        Assert.NotEqual(softDeletedRepositoryId, repository.Id);
        Assert.Single(await context.Repositories.ToListAsync());
        Assert.False(await context.Repositories.AnyAsync(r => r.Id == softDeletedRepositoryId));
        Assert.Null((await context.TokenUsages.SingleAsync()).RepositoryId);
    }

    [Fact]
    public async Task RegenerateAsync_WhenAdminIsNotOwner_AllowsRegeneration()
    {
        using var context = CreateContext();
        await SeedRepositoryWithDocumentAsync(context, RepositoryStatus.Failed);
        var service = CreateService(
            context,
            new RepositoryAnalyzerOptions(),
            userId: "admin-1",
            isAdmin: true);

        var result = await RegenerateAsync(service, new RegenerateRequest
        {
            Owner = "AIDotNet",
            Repo = "OpenCowork"
        });

        Assert.True(result.Success);
        Assert.Equal(RepositoryStatus.Pending, (await context.Repositories.SingleAsync()).Status);
    }

    [Fact]
    public async Task RegenerateAsync_WhenNonOwnerNonAdmin_ReturnsForbid()
    {
        using var context = CreateContext();
        await SeedRepositoryWithDocumentAsync(context, RepositoryStatus.Failed);
        var service = CreateService(
            context,
            new RepositoryAnalyzerOptions(),
            userId: "other-user");

        var result = await service.RegenerateAsync(new RegenerateRequest
        {
            Owner = "AIDotNet",
            Repo = "OpenCowork"
        });

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(result);
    }

    [Fact]
    public async Task RegenerateAsync_WhenRepositoryFailed_ClearsDocumentsForFreshRun()
    {
        using var context = CreateContext();
        await SeedRepositoryWithDocumentAsync(
            context,
            RepositoryStatus.Failed);
        var service = CreateService(context, new RepositoryAnalyzerOptions());

        var result = await RegenerateAsync(service, new RegenerateRequest
        {
            Owner = "AIDotNet",
            Repo = "OpenCowork"
        });

        Assert.True(result.Success);
        Assert.Equal(RepositoryStatus.Pending, (await context.Repositories.SingleAsync()).Status);
        Assert.Empty(await context.DocCatalogs.ToListAsync());
        Assert.Empty(await context.DocFiles.ToListAsync());
    }

    [Fact]
    public async Task RegenerateAsync_WhenRepositoryCompleted_ClearsDocumentsForFreshRun()
    {
        using var context = CreateContext();
        await SeedRepositoryWithDocumentAsync(context, RepositoryStatus.Completed);
        var service = CreateService(context, new RepositoryAnalyzerOptions());

        var result = await RegenerateAsync(service, new RegenerateRequest
        {
            Owner = "AIDotNet",
            Repo = "OpenCowork"
        });

        Assert.True(result.Success);
        Assert.Equal(RepositoryStatus.Pending, (await context.Repositories.SingleAsync()).Status);
        Assert.Empty(await context.DocCatalogs.ToListAsync());
        Assert.Empty(await context.DocFiles.ToListAsync());
    }

    [Fact]
    public async Task RegenerateAsync_ClearsBranchCommitAndOnlyUnfinishedIncrementalTasks()
    {
        using var context = CreateContext();
        var seeded = await SeedRepositoryWithDocumentAsync(context, RepositoryStatus.Completed);
        context.IncrementalUpdateTasks.AddRange(
            new IncrementalUpdateTask
            {
                Id = "pending-task",
                RepositoryId = seeded.RepositoryId,
                BranchId = seeded.BranchId,
                Status = IncrementalUpdateStatus.Pending
            },
            new IncrementalUpdateTask
            {
                Id = "processing-task",
                RepositoryId = seeded.RepositoryId,
                BranchId = seeded.BranchId,
                Status = IncrementalUpdateStatus.Processing
            },
            new IncrementalUpdateTask
            {
                Id = "completed-task",
                RepositoryId = seeded.RepositoryId,
                BranchId = seeded.BranchId,
                Status = IncrementalUpdateStatus.Completed
            },
            new IncrementalUpdateTask
            {
                Id = "failed-task",
                RepositoryId = seeded.RepositoryId,
                BranchId = seeded.BranchId,
                Status = IncrementalUpdateStatus.Failed
            });
        await context.SaveChangesAsync();
        var service = CreateService(context, new RepositoryAnalyzerOptions());

        var result = await RegenerateAsync(service, new RegenerateRequest
        {
            Owner = "AIDotNet",
            Repo = "OpenCowork"
        });

        Assert.True(result.Success);
        var branch = await context.RepositoryBranches.SingleAsync();
        Assert.Null(branch.LastCommitId);
        Assert.Null(branch.LastProcessedAt);
        var remainingTaskIds = await context.IncrementalUpdateTasks
            .OrderBy(task => task.Id)
            .Select(task => task.Id)
            .ToListAsync();
        Assert.Equal(new[] { "completed-task", "failed-task" }, remainingTaskIds);
    }

    [Fact]
    public async Task AdminRegenerateRepositoryAsync_WhenBranchGenerationLockExists_ReturnsConflictWithoutCleaning()
    {
        using var context = CreateContext();
        var seeded = await SeedRepositoryWithDocumentAsync(context, RepositoryStatus.Completed);
        context.RepositoryGenerationLocks.Add(new RepositoryGenerationLock
        {
            Id = "branch-lock",
            RepositoryId = seeded.RepositoryId,
            OwnerType = RepositoryGenerationLockOwnerType.BranchTask,
            OwnerId = "branch-task",
            Scope = RepositoryGenerationLockScope.Branch
        });
        await context.SaveChangesAsync();
        var service = CreateAdminService(context);

        var result = await service.RegenerateRepositoryAsync(seeded.RepositoryId);

        Assert.False(result.Success);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(RepositoryStatus.Completed, (await context.Repositories.SingleAsync()).Status);
        Assert.Single(await context.DocCatalogs.ToListAsync());
        Assert.Single(await context.DocFiles.ToListAsync());
        Assert.NotNull(await context.RepositoryGenerationLocks.SingleOrDefaultAsync(item => item.Id == "branch-lock"));
    }

    [Fact]
    public async Task AdminRegenerateRepositoryAsync_WhenQueued_KeepsRepositoryLockForWorker()
    {
        using var context = CreateContext();
        var seeded = await SeedRepositoryWithDocumentAsync(context, RepositoryStatus.Completed);
        var service = CreateAdminService(context);

        var result = await service.RegenerateRepositoryAsync(seeded.RepositoryId);

        Assert.True(result.Success);
        Assert.Equal(RepositoryStatus.Pending, (await context.Repositories.SingleAsync()).Status);
        var generationLock = await context.RepositoryGenerationLocks.SingleAsync();
        Assert.Equal(seeded.RepositoryId, generationLock.RepositoryId);
        Assert.Equal(RepositoryGenerationLockOwnerType.Repository, generationLock.OwnerType);
        Assert.Equal(seeded.RepositoryId, generationLock.OwnerId);
    }

    [Fact]
    public async Task AdminBatchRegenerateRepositoriesAsync_QueuesEligibleRepositoriesAndReportsActiveOnes()
    {
        using var context = CreateContext();
        var eligible = await SeedRepositoryWithDocumentAsync(context, RepositoryStatus.Completed);
        var active = await SeedRepositoryWithDocumentAsync(context, RepositoryStatus.Pending);
        var service = CreateAdminService(context);

        var result = await service.BatchRegenerateRepositoriesAsync([
            eligible.RepositoryId,
            active.RepositoryId
        ]);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.FailedCount);

        var queuedItem = Assert.Single(result.Results, item => item.Id == eligible.RepositoryId);
        Assert.True(queuedItem.Success);
        var rejectedItem = Assert.Single(result.Results, item => item.Id == active.RepositoryId);
        Assert.False(rejectedItem.Success);
        Assert.Contains("正在处理中", rejectedItem.Message);

        Assert.Equal(
            RepositoryStatus.Pending,
            (await context.Repositories.SingleAsync(item => item.Id == eligible.RepositoryId)).Status);
        Assert.True(await context.DocCatalogs.AnyAsync(item => item.Id == active.CatalogId));
        Assert.False(await context.DocCatalogs.AnyAsync(item => item.Id == eligible.CatalogId));
        Assert.False(await context.DocFiles.AnyAsync(item => item.Id == eligible.DocFileId));

        var generationLock = await context.RepositoryGenerationLocks
            .SingleAsync(item => item.RepositoryId == eligible.RepositoryId);
        Assert.Equal(RepositoryGenerationLockOwnerType.Repository, generationLock.OwnerType);
        Assert.Equal(eligible.RepositoryId, generationLock.OwnerId);
    }

    [Fact]
    public async Task AdminBatchRegenerateRepositoriesAsync_WhenInputIsEmpty_ReturnsEmptyResult()
    {
        using var context = CreateContext();
        var service = CreateAdminService(context);

        var result = await service.BatchRegenerateRepositoriesAsync([" ", "\t"]);

        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(result.Results);
        Assert.Empty(await context.RepositoryGenerationLocks.ToListAsync());
    }

    [Fact]
    public async Task AdminBatchRegenerateRepositoriesAsync_NormalizesDuplicateAndUnknownIds()
    {
        using var context = CreateContext();
        var seeded = await SeedRepositoryWithDocumentAsync(context, RepositoryStatus.Completed);
        const string unknownRepositoryId = "missing-repository";
        var service = CreateAdminService(context);

        var result = await service.BatchRegenerateRepositoriesAsync([
            " ",
            $" {seeded.RepositoryId} ",
            seeded.RepositoryId,
            unknownRepositoryId
        ]);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(
            new[] { seeded.RepositoryId, unknownRepositoryId },
            result.Results.Select(item => item.Id));

        var unknownItem = Assert.Single(result.Results, item => item.Id == unknownRepositoryId);
        Assert.False(unknownItem.Success);
        Assert.Equal(unknownRepositoryId, unknownItem.RepoName);
        Assert.Contains("仓库不存在", unknownItem.Message);
    }

    [Fact]
    public async Task AdminBatchRegenerateRepositoriesAsync_WhenOneItemThrows_ContinuesWithRemainingRepositories()
    {
        using var context = CreateContext();
        var failing = await SeedRepositoryWithDocumentAsync(context, RepositoryStatus.Completed);
        var succeeding = await SeedRepositoryWithDocumentAsync(context, RepositoryStatus.Completed);
        var service = CreateAdminService(
            context,
            fullRegenerationCleaner: new ThrowingRepositoryCleaner(failing.RepositoryId));

        var result = await service.BatchRegenerateRepositoriesAsync([
            failing.RepositoryId,
            succeeding.RepositoryId
        ]);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(1, result.FailedCount);

        var failedItem = Assert.Single(result.Results, item => item.Id == failing.RepositoryId);
        Assert.False(failedItem.Success);
        Assert.Equal("触发全量重生成失败，请稍后重试", failedItem.Message);

        var succeededItem = Assert.Single(result.Results, item => item.Id == succeeding.RepositoryId);
        Assert.True(succeededItem.Success);

        Assert.Equal(
            RepositoryStatus.Completed,
            (await context.Repositories.SingleAsync(item => item.Id == failing.RepositoryId)).Status);
        Assert.Equal(
            RepositoryStatus.Pending,
            (await context.Repositories.SingleAsync(item => item.Id == succeeding.RepositoryId)).Status);
        Assert.True(await context.DocCatalogs.AnyAsync(item => item.Id == failing.CatalogId));
        Assert.False(await context.DocCatalogs.AnyAsync(item => item.Id == succeeding.CatalogId));
        Assert.False(await context.RepositoryGenerationLocks.AnyAsync(item => item.RepositoryId == failing.RepositoryId));
        Assert.True(await context.RepositoryGenerationLocks.AnyAsync(item => item.RepositoryId == succeeding.RepositoryId));
    }

    [Fact]
    public void RepositoryStatus_ProcessingMatchesAdminApiStatusValue()
    {
        Assert.Equal(0, (int)RepositoryStatus.Pending);
        Assert.Equal(1, (int)RepositoryStatus.Processing);
    }

    [Theory]
    [InlineData(RepositoryStatus.Pending)]
    [InlineData(RepositoryStatus.Processing)]
    public async Task ReevaluateScanPlanAsync_WhenRepositoryPendingOrProcessing_RejectsWithoutMutatingPlan(RepositoryStatus status)
    {
        using var context = CreateContext();
        var updatedAt = DateTime.UtcNow.AddMinutes(-15);
        context.Repositories.Add(new Repository
        {
            Id = "repo-processing",
            OwnerUserId = "user-1",
            GitUrl = "https://github.com/AIDotNet/OpenCowork.git",
            OrgName = "AIDotNet",
            RepoName = "OpenCowork",
            Status = status,
            ScanDepthMode = RepositoryScanDepthMode.Auto,
            DirectoryTreeDepthOverride = 3,
            FileListDepthOverride = 2,
            MaxTreeNodes = 900,
            MaxFilesPerDirectory = 18,
            MaxTotalFiles = 400,
            ExtraExcludedDirsJson = "[\"vendor\"]",
            ScanProfileHash = "hash-before",
            ScanProfileReason = "Existing auto plan",
            ScanProfileConfidence = 0.8,
            ScanProfileUpdatedAt = updatedAt
        });
        context.RepositoryBranches.Add(new RepositoryBranch
        {
            Id = "branch-processing",
            RepositoryId = "repo-processing",
            BranchName = "main",
            LastCommitId = "abc123",
            LastProcessedAt = updatedAt
        });
        await context.SaveChangesAsync();
        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        var service = CreateAdminService(context, analyzer.Object);

        var result = await service.ReevaluateScanPlanAsync("repo-processing");

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("处理中", result.Message);
        analyzer.Verify(
            item => item.PrepareWorkspaceAsync(
                It.IsAny<Repository>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        var repository = await context.Repositories.AsNoTracking().SingleAsync();
        Assert.Equal(status, repository.Status);
        Assert.Equal(RepositoryScanDepthMode.Auto, repository.ScanDepthMode);
        Assert.Equal(3, repository.DirectoryTreeDepthOverride);
        Assert.Equal(2, repository.FileListDepthOverride);
        Assert.Equal(900, repository.MaxTreeNodes);
        Assert.Equal(18, repository.MaxFilesPerDirectory);
        Assert.Equal(400, repository.MaxTotalFiles);
        Assert.Equal("[\"vendor\"]", repository.ExtraExcludedDirsJson);
        Assert.Equal("hash-before", repository.ScanProfileHash);
        Assert.Equal("Existing auto plan", repository.ScanProfileReason);
        Assert.True(repository.ScanProfileConfidence.HasValue);
        Assert.Equal(0.8, repository.ScanProfileConfidence.Value);
        Assert.Equal(updatedAt, repository.ScanProfileUpdatedAt);
    }

    [Fact]
    public async Task UpdateScanPlanAsync_WhenSwitchingManualToAutoWithoutPlan_ClearsManualOverrides()
    {
        using var context = CreateContext();
        context.Repositories.Add(new Repository
        {
            Id = "repo-manual",
            OwnerUserId = "user-1",
            GitUrl = "https://github.com/AIDotNet/OpenCowork.git",
            OrgName = "AIDotNet",
            RepoName = "OpenCowork",
            Status = RepositoryStatus.Completed,
            ScanDepthMode = RepositoryScanDepthMode.Manual,
            DirectoryTreeDepthOverride = 4,
            FileListDepthOverride = 3,
            MaxTreeNodes = 1400,
            MaxFilesPerDirectory = 25,
            MaxTotalFiles = 700,
            ExtraExcludedDirsJson = "[\"vendor\"]"
        });
        await context.SaveChangesAsync();
        var service = CreateAdminService(context);

        var plan = await service.UpdateScanPlanAsync(
            "repo-manual",
            new UpdateRepositoryScanPlanRequest { Mode = "Auto" });

        Assert.NotNull(plan);
        Assert.Equal("Global", plan.Source);
        var repository = await context.Repositories.SingleAsync();
        Assert.Equal(RepositoryScanDepthMode.Auto, repository.ScanDepthMode);
        Assert.Null(repository.DirectoryTreeDepthOverride);
        Assert.Null(repository.FileListDepthOverride);
        Assert.Null(repository.MaxTreeNodes);
        Assert.Null(repository.MaxFilesPerDirectory);
        Assert.Null(repository.MaxTotalFiles);
        Assert.Null(repository.ExtraExcludedDirsJson);
    }

    [Fact]
    public async Task UpdateScanPlanAsync_WhenSavingAutoWithoutPlan_PreservesSavedAutoPlan()
    {
        using var context = CreateContext();
        context.Repositories.Add(new Repository
        {
            Id = "repo-auto",
            OwnerUserId = "user-1",
            GitUrl = "https://github.com/AIDotNet/OpenCowork.git",
            OrgName = "AIDotNet",
            RepoName = "OpenCowork",
            Status = RepositoryStatus.Completed,
            ScanDepthMode = RepositoryScanDepthMode.Auto,
            DirectoryTreeDepthOverride = 3,
            FileListDepthOverride = 2,
            MaxTreeNodes = 900,
            MaxFilesPerDirectory = 18,
            MaxTotalFiles = 400,
            ExtraExcludedDirsJson = "[\"vendor\"]",
            ScanProfileHash = "hash-1",
            ScanProfileReason = "Existing auto plan",
            ScanProfileConfidence = 0.8,
            ScanProfileUpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = CreateAdminService(context);

        var plan = await service.UpdateScanPlanAsync(
            "repo-auto",
            new UpdateRepositoryScanPlanRequest { Mode = "Auto" });

        Assert.NotNull(plan);
        Assert.Equal("Auto", plan.Source);
        Assert.Equal(3, plan.DirectoryTreeDepth);
        Assert.Equal(2, plan.FileListDepth);
        var repository = await context.Repositories.SingleAsync();
        Assert.Equal(3, repository.DirectoryTreeDepthOverride);
        Assert.Equal(2, repository.FileListDepthOverride);
        Assert.Equal(900, repository.MaxTreeNodes);
        Assert.Equal("hash-1", repository.ScanProfileHash);
    }

    private static RepositoryService CreateService(
        TestDbContext context,
        RepositoryAnalyzerOptions analyzerOptions,
        string userId = "user-1",
        IGitPlatformService? gitPlatformService = null,
        bool isAdmin = false)
    {
        return new RepositoryService(
            context,
            gitPlatformService ?? Mock.Of<IGitPlatformService>(),
            new TestUserContext(userId, isAdmin),
            Mock.Of<IGitHubAppService>(),
            Mock.Of<IOrganizationService>(),
            new RepositoryFullRegenerationCleaner(),
            new RepositoryGenerationLockService(context),
            new MemoryCacheAdapter(new MemoryCache(new MemoryCacheOptions())),
            Options.Create(analyzerOptions));
    }

    private static async Task<RegenerateResponse> RegenerateAsync(
        RepositoryService service,
        RegenerateRequest request)
    {
        var result = await service.RegenerateAsync(request);
        var ok = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<RegenerateResponse>>(result);
        return ok.Value!;
    }

    private static async Task<(string RepositoryId, string BranchId, string CatalogId, string DocFileId)> SeedRepositoryWithDocumentAsync(
        TestDbContext context,
        RepositoryStatus status)
    {
        var repositoryId = Guid.NewGuid().ToString();
        var branchId = Guid.NewGuid().ToString();
        var branchLanguageId = Guid.NewGuid().ToString();
        var docFileId = Guid.NewGuid().ToString();
        var catalogId = Guid.NewGuid().ToString();

        context.Repositories.Add(new Repository
        {
            Id = repositoryId,
            OwnerUserId = "user-1",
            GitUrl = "https://github.com/AIDotNet/OpenCowork.git",
            OrgName = "AIDotNet",
            RepoName = "OpenCowork",
            Status = status
        });
        context.RepositoryBranches.Add(new RepositoryBranch
        {
            Id = branchId,
            RepositoryId = repositoryId,
            BranchName = "main",
            LastCommitId = "abc123",
            LastProcessedAt = DateTime.UtcNow
        });
        context.BranchLanguages.Add(new BranchLanguage
        {
            Id = branchLanguageId,
            RepositoryBranchId = branchId,
            LanguageCode = "zh",
            IsDefault = true
        });
        context.DocFiles.Add(new DocFile
        {
            Id = docFileId,
            BranchLanguageId = branchLanguageId,
            Content = "# Existing"
        });
        context.DocCatalogs.Add(new DocCatalog
        {
            Id = catalogId,
            BranchLanguageId = branchLanguageId,
            Title = "Existing",
            Path = "existing",
            DocFileId = docFileId
        });

        await context.SaveChangesAsync();
        return (repositoryId, branchId, catalogId, docFileId);
    }

    private static AdminRepositoryService CreateAdminService(
        TestDbContext context,
        IRepositoryAnalyzer? repositoryAnalyzer = null,
        IRepositoryFullRegenerationCleaner? fullRegenerationCleaner = null)
    {
        return new AdminRepositoryService(
            context,
            Mock.Of<IGitPlatformService>(),
            repositoryAnalyzer ?? Mock.Of<IRepositoryAnalyzer>(),
            Mock.Of<IWikiGenerator>(),
            fullRegenerationCleaner ?? new RepositoryFullRegenerationCleaner(),
            CreateScanPlanResolver(),
            new RepositoryGenerationLockService(context));
    }

    private static RepositoryScanPlanResolver CreateScanPlanResolver()
    {
        var monitor = new Mock<IOptionsMonitor<WikiGeneratorOptions>>();
        monitor.SetupGet(item => item.CurrentValue).Returns(new WikiGeneratorOptions());
        return new RepositoryScanPlanResolver(monitor.Object);
    }

    private sealed class ThrowingRepositoryCleaner(string repositoryId) : IRepositoryFullRegenerationCleaner
    {
        private readonly RepositoryFullRegenerationCleaner _inner = new();

        public Task CleanAsync(IContext context, Repository repository, CancellationToken cancellationToken = default)
        {
            if (repository.Id == repositoryId)
            {
                throw new InvalidOperationException("simulated cleaner failure");
            }

            return _inner.CleanAsync(context, repository, cancellationToken);
        }
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }

    private static async Task<TestDbContext> CreateSqliteContextAsync()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        var context = new TestDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenDeepWiki.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static MemoryStream CreateArchiveStream(params (string path, string content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class TestUserContext(string? userId, bool isAdmin = false) : IUserContext
    {
        public string? UserId { get; } = userId;
        public string? UserName => "token帅比";
        public string? Email => "token@example.com";
        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(UserId);
        public ClaimsPrincipal? User { get; } = CreatePrincipal(userId, isAdmin);

        private static ClaimsPrincipal? CreatePrincipal(string? userId, bool isAdmin)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId)
            };
            if (isAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : MasterDbContext(options)
    {
    }
}
