using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Models;
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.GitHub;
using OpenDeepWiki.Services.Organizations;
using OpenDeepWiki.Services.Repositories;
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
    }

    private static RepositoryService CreateService(
        TestDbContext context,
        RepositoryAnalyzerOptions analyzerOptions,
        string userId = "user-1",
        IGitPlatformService? gitPlatformService = null)
    {
        return new RepositoryService(
            context,
            gitPlatformService ?? Mock.Of<IGitPlatformService>(),
            new TestUserContext(userId),
            Mock.Of<IGitHubAppService>(),
            Mock.Of<IOrganizationService>(),
            Options.Create(analyzerOptions));
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
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

    private sealed class TestUserContext(string? userId) : IUserContext
    {
        public string? UserId { get; } = userId;
        public string? UserName => "token帅比";
        public string? Email => "token@example.com";
        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(UserId);
        public System.Security.Claims.ClaimsPrincipal? User => null;
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : MasterDbContext(options)
    {
    }
}
