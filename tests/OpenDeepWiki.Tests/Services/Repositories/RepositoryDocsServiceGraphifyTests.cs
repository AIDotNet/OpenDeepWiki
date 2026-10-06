using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenDeepWiki.Cache.Abstractions;
using OpenDeepWiki.Cache.Memory;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Graphify;
using OpenDeepWiki.Services.Repositories;
using System.IO.Compression;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class RepositoryDocsServiceGraphifyTests
{
    private const string Owner = "owner";
    private const string Repo = "repo";

    [Fact]
    public async Task GetTreeAsync_WhenCompletedGraphifyHtmlExists_ReturnsGraphifyFlag()
    {
        await using var context = CreateContext();
        var seed = SeedRepositoryWithDocument(context);
        var htmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(htmlPath, "<html><body>graph</body></html>");
        try
        {
            context.GraphifyArtifacts.Add(new GraphifyArtifact
            {
                Id = Guid.NewGuid().ToString(),
                RepositoryId = seed.RepositoryId,
                RepositoryBranchId = seed.BranchId,
                Status = GraphifyArtifactStatus.Completed,
                EntryFilePath = htmlPath
            });
            await context.SaveChangesAsync();
            var service = CreateService(context);

            var response = await service.GetTreeAsync(Owner, Repo, "main", "en");

            Assert.True(response.HasGraphifyArtifact);
            Assert.Equal((int)GraphifyArtifactStatus.Completed, response.GraphifyStatus);
            Assert.Equal(nameof(GraphifyArtifactStatus.Completed), response.GraphifyStatusName);
            Assert.NotEmpty(response.Nodes);
        }
        finally
        {
            File.Delete(htmlPath);
        }
    }

    [Fact]
    public async Task GetTreeAsync_WhenGraphifyHtmlIsMissing_ReturnsStatusWithoutFlag()
    {
        await using var context = CreateContext();
        var seed = SeedRepositoryWithDocument(context);
        context.GraphifyArtifacts.Add(new GraphifyArtifact
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = seed.RepositoryId,
            RepositoryBranchId = seed.BranchId,
            Status = GraphifyArtifactStatus.Completed,
            EntryFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html")
        });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var response = await service.GetTreeAsync(Owner, Repo, "main", "en");

        Assert.False(response.HasGraphifyArtifact);
        Assert.Equal((int)GraphifyArtifactStatus.Completed, response.GraphifyStatus);
        Assert.Equal(nameof(GraphifyArtifactStatus.Completed), response.GraphifyStatusName);
    }

    [Fact]
    public async Task GetTreeAsync_WhenProcessingRepositoryRouteCasingDiffers_ReturnsProcessingState()
    {
        await using var context = CreateContext();
        SeedRepository(context, "AIDotNet", "OpenCowork", RepositoryStatus.Processing);
        var service = CreateService(context);

        var response = await service.GetTreeAsync("aidotnet", "opencowork");

        Assert.True(response.Exists);
        Assert.Equal("AIDotNet", response.Owner);
        Assert.Equal("OpenCowork", response.Repo);
        Assert.Equal(RepositoryStatus.Processing, response.Status);
        Assert.Empty(response.Nodes);
    }

    [Fact]
    public async Task GetGraphifyAsync_WhenArtifactExists_ReturnsPhysicalHtmlFile()
    {
        await using var context = CreateContext();
        var seed = SeedRepositoryWithDocument(context);
        var htmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(htmlPath, "<html><body>graph</body></html>");
        try
        {
            var artifact = new GraphifyArtifact
            {
                Id = Guid.NewGuid().ToString(),
                RepositoryId = seed.RepositoryId,
                RepositoryBranchId = seed.BranchId,
                Status = GraphifyArtifactStatus.Completed,
                EntryFilePath = htmlPath
            };
            var graphify = new Mock<IGraphifyArtifactService>(MockBehavior.Strict);
            graphify
                .Setup(service => service.GetCompletedArtifactAsync(Owner, Repo, "main", It.IsAny<CancellationToken>()))
                .ReturnsAsync(artifact);
            var service = CreateService(context, graphify.Object);

            var result = await service.GetGraphifyAsync(Owner, Repo, "main");

            var (httpContext, responseBody) = CreateHttpContext();
            await using (responseBody)
            {
                await result.ExecuteAsync(httpContext);

                responseBody.Position = 0;
                var body = await new StreamReader(responseBody).ReadToEndAsync();
                Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
                Assert.Equal("text/html; charset=utf-8", httpContext.Response.ContentType);
                Assert.Contains("<body>graph</body>", body);
            }
        }
        finally
        {
            File.Delete(htmlPath);
        }
    }

    [Fact]
    public async Task GetGraphifyAsync_WhenArtifactIsNotCompleted_ReturnsNotFound()
    {
        await using var context = CreateContext();
        SeedRepositoryWithDocument(context);
        var graphify = new Mock<IGraphifyArtifactService>(MockBehavior.Strict);
        graphify
            .Setup(service => service.GetCompletedArtifactAsync(Owner, Repo, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GraphifyArtifact?)null);
        var service = CreateService(context, graphify.Object);

        var result = await service.GetGraphifyAsync(Owner, Repo, "main");

        var (httpContext, responseBody) = CreateHttpContext();
        await using (responseBody)
        {
            await result.ExecuteAsync(httpContext);
        }

        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task GetGraphifyReportAsync_WhenReportExists_ReturnsMarkdown()
    {
        await using var context = CreateContext();
        var seed = SeedRepositoryWithDocument(context);
        var reportPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(reportPath, "# Graph Report");
        try
        {
            var artifact = new GraphifyArtifact
            {
                Id = Guid.NewGuid().ToString(),
                RepositoryId = seed.RepositoryId,
                RepositoryBranchId = seed.BranchId,
                Status = GraphifyArtifactStatus.Completed,
                ReportPath = reportPath
            };
            var graphify = new Mock<IGraphifyArtifactService>(MockBehavior.Strict);
            graphify
                .Setup(service => service.GetCompletedArtifactAsync(Owner, Repo, "main", It.IsAny<CancellationToken>()))
                .ReturnsAsync(artifact);
            var service = CreateService(context, graphify.Object);

            var result = await service.GetGraphifyReportAsync(Owner, Repo, "main");

            var (httpContext, responseBody) = CreateHttpContext();
            await using (responseBody)
            {
                await result.ExecuteAsync(httpContext);

                responseBody.Position = 0;
                var body = await new StreamReader(responseBody).ReadToEndAsync();
                Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
                Assert.Equal("text/markdown; charset=utf-8", httpContext.Response.ContentType);
                Assert.Contains("# Graph Report", body);
            }
        }
        finally
        {
            File.Delete(reportPath);
        }
    }

    [Fact]
    public async Task GetGraphifyReportAsync_WhenReportIsMissing_ReturnsNotFound()
    {
        await using var context = CreateContext();
        var seed = SeedRepositoryWithDocument(context);
        var artifact = new GraphifyArtifact
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = seed.RepositoryId,
            RepositoryBranchId = seed.BranchId,
            Status = GraphifyArtifactStatus.Completed,
            ReportPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md")
        };
        var graphify = new Mock<IGraphifyArtifactService>(MockBehavior.Strict);
        graphify
            .Setup(service => service.GetCompletedArtifactAsync(Owner, Repo, "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifact);
        var service = CreateService(context, graphify.Object);

        var result = await service.GetGraphifyReportAsync(Owner, Repo, "main");

        var (httpContext, responseBody) = CreateHttpContext();
        await using (responseBody)
        {
            await result.ExecuteAsync(httpContext);
        }

        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task ExportAsync_WhenSkillExportEnabled_ReturnsSkillZipLayout()
    {
        await using var context = CreateContext();
        SeedRepositoryWithDocument(context);
        var service = CreateService(context, cache: new TestCache());

        var result = await service.ExportAsync(Owner, Repo, "main", "en");

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/zip", file.ContentType);
        Assert.Equal("owner-repo-main-en-skill.zip", file.FileDownloadName);

        using var zipStream = new MemoryStream(file.FileContents);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        Assert.NotNull(archive.GetEntry("SKILL.md"));
        Assert.NotNull(archive.GetEntry("references/docs/Overview.md"));

        var skillMarkdown = await ReadZipEntryAsync(archive, "SKILL.md");
        var docMarkdown = await ReadZipEntryAsync(archive, "references/docs/Overview.md");

        Assert.Contains("name: owner-repo", skillMarkdown);
        Assert.Contains("references/docs/Overview.md", skillMarkdown);
        Assert.Equal("# Overview", docMarkdown);
    }

    [Fact]
    public async Task ExportAsync_WhenSkillExportDisabled_ReturnsBadRequest()
    {
        await using var context = CreateContext();
        SeedRepositoryWithDocument(context, generateSkill: false);
        var service = CreateService(context);

        var result = await service.ExportAsync(Owner, Repo, "main", "en");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("SKILL export", badRequest.Value?.ToString());
    }

    private static (DefaultHttpContext Context, MemoryStream ResponseBody) CreateHttpContext()
    {
        var responseBody = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider()
        };
        context.Response.Body = responseBody;
        return (context, responseBody);
    }

    private static RepositoryDocsService CreateService(
        TestDbContext context,
        IGraphifyArtifactService? graphifyArtifactService = null,
        ICache? cache = null)
    {
        var gitPlatform = new Mock<IGitPlatformService>(MockBehavior.Strict).Object;
        return new RepositoryDocsService(
            context,
            gitPlatform,
            // GetTreeAsync 会读取缓存版本号与缓存条目，使用真实内存实现
            cache ?? new MemoryCacheAdapter(new MemoryCache(new MemoryCacheOptions())),
            graphifyArtifactService ?? new Mock<IGraphifyArtifactService>(MockBehavior.Strict).Object,
            new RepositorySkillMarkdownBuilder());
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    private static async Task<string> ReadZipEntryAsync(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"Missing zip entry: {name}");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static SeededRepository SeedRepositoryWithDocument(TestDbContext context, bool generateSkill = true)
    {
        var repositoryId = Guid.NewGuid().ToString();
        var branchId = Guid.NewGuid().ToString();
        var languageId = Guid.NewGuid().ToString();
        var docFileId = Guid.NewGuid().ToString();

        context.Repositories.Add(new Repository
        {
            Id = repositoryId,
            OrgName = Owner,
            RepoName = Repo,
            GitUrl = $"https://github.com/{Owner}/{Repo}.git",
            OwnerUserId = Guid.NewGuid().ToString(),
            Status = RepositoryStatus.Completed,
            GenerateSkill = generateSkill
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
            Content = "# Overview"
        });

        context.DocCatalogs.Add(new DocCatalog
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = languageId,
            Path = "overview",
            Title = "Overview",
            Order = 1,
            DocFileId = docFileId
        });

        context.SaveChanges();
        return new SeededRepository(repositoryId, branchId);
    }

    private static void SeedRepository(
        TestDbContext context,
        string owner,
        string repo,
        RepositoryStatus status)
    {
        context.Repositories.Add(new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OrgName = owner,
            RepoName = repo,
            GitUrl = $"https://github.com/{owner}/{repo}.git",
            OwnerUserId = Guid.NewGuid().ToString(),
            Status = status
        });
        context.SaveChanges();
    }

    private sealed record SeededRepository(string RepositoryId, string BranchId);

    private sealed class TestCache : ICache
    {
        private readonly Dictionary<string, object?> _items = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.TryGetValue(key, out var value) ? (T?)value : default);
        }

        public Task SetAsync<T>(string key, T value, CacheEntryOptions options, CancellationToken cancellationToken = default)
        {
            _items[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _items.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.ContainsKey(key));
        }

        public Task<ICacheLock?> AcquireLockAsync(string key, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ICacheLock?>(new TestCacheLock());
        }
    }

    private sealed class TestCacheLock : ICacheLock
    {
        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options)
        : OpenDeepWiki.EFCore.MasterDbContext(options);
}
