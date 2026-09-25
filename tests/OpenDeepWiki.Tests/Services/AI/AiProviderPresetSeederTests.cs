using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenDeepWiki.Entities;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Services.AI;
using OpenDeepWiki.Services.Admin;
using Xunit;

namespace OpenDeepWiki.Tests.Services.AI;

public class AiProviderPresetSeederTests
{
    [Fact]
    public void Catalog_LoadsOpenCoworkBuiltInProviders()
    {
        var catalog = new AiProviderPresetCatalog();

        Assert.Equal(28, catalog.Presets.Count);
        Assert.Equal(444, catalog.Presets.Sum(p => p.DefaultModels.Count));
        Assert.Contains(catalog.Presets, p => p.BuiltinId == "routin-ai" && p.DefaultEnabled == true);
        Assert.Contains(catalog.Presets, p => p.BuiltinId == "codex-oauth" && p.AuthMode == "oauth");
        Assert.Contains(catalog.Presets.Single(p => p.BuiltinId == "openai").DefaultModels,
            model => model.Id == "gpt-5.2");
        Assert.Contains(catalog.Presets,
            p => p.BuiltinId == "orcarouter" &&
                 p.DefaultBaseUrl == "https://api.orcarouter.ai/v1" &&
                 p.DefaultModels.Any(m => m.Id == "orcarouter/auto"));
        Assert.Contains(catalog.Presets,
            p => p.BuiltinId == "requesty" &&
                 p.DefaultBaseUrl == "https://router.requesty.ai/v1" &&
                 p.DefaultModels.Any(m => m.Id == "claude-sonnet-4-6"));
    }

    [Fact]
    public async Task EnsureBuiltInProvidersAsync_IsIdempotent()
    {
        await using var context = CreateContext();
        var seeder = CreateSeeder(context);

        await seeder.EnsureBuiltInProvidersAsync();
        await seeder.EnsureBuiltInProvidersAsync();

        Assert.Equal(28, await context.AiProviderConfigs.CountAsync(p => !p.IsDeleted));
        Assert.Equal(444, await context.AiModelConfigs.CountAsync(m => !m.IsDeleted));

        var codex = await context.AiProviderConfigs.SingleAsync(p => p.Name == "codex-oauth");
        Assert.True(codex.IsBuiltIn);
        Assert.Equal("OAuth", codex.AuthType);
        Assert.Equal("OpenAIResponses", codex.ProviderType);
        Assert.Equal("gpt-5.1-codex", codex.DefaultModelId);
        Assert.Contains("@lobehub/icons-static-png", codex.IconUrl!);
        Assert.Contains("User-Agent", codex.RequestOverridesJson);

        var routin = await context.AiProviderConfigs.SingleAsync(p => p.Name == "routin-ai");
        var routinDeepSeek = await context.AiModelConfigs.SingleAsync(m =>
            m.ProviderId == routin.Id && m.ModelId == "deepseek-v4-flash");
        Assert.Equal("DeepSeekOpenAI", routinDeepSeek.ProviderType);
        var routinXiaomi = await context.AiModelConfigs.SingleAsync(m =>
            m.ProviderId == routin.Id && m.ModelId == "mimo-v2.5-pro");
        Assert.Equal("DeepSeekOpenAI", routinXiaomi.ProviderType);
        Assert.Equal(3m, routinXiaomi.InputTokenPrice);
        Assert.Equal(6m, routinXiaomi.OutputTokenPrice);
        Assert.Equal(0.025m, routinXiaomi.CacheHitTokenPrice);
        Assert.Equal(0m, routinXiaomi.CacheCreationTokenPrice);

        var openai = await context.AiProviderConfigs.SingleAsync(p => p.Name == "openai");
        var gpt52 = await context.AiModelConfigs.SingleAsync(m =>
            m.ProviderId == openai.Id && m.ModelId == "gpt-5.2");
        Assert.Equal("OpenAIResponses", gpt52.ProviderType);

        var deepseek = await context.AiProviderConfigs.SingleAsync(p => p.Name == "deepseek");
        Assert.Equal("DeepSeekOpenAI", deepseek.ProviderType);

        var xiaomi = await context.AiProviderConfigs.SingleAsync(p => p.Name == "xiaomi");
        var xiaomiModel = await context.AiModelConfigs.SingleAsync(m =>
            m.ProviderId == xiaomi.Id && m.ModelId == "mimo-v2.5-pro");
        Assert.Equal("DeepSeekOpenAI", xiaomi.ProviderType);
        Assert.Equal("DeepSeekOpenAI", xiaomiModel.ProviderType);

        var xiaomiCoding = await context.AiProviderConfigs.SingleAsync(p => p.Name == "xiaomi-coding");
        var xiaomiCodingModels = await context.AiModelConfigs
            .Where(m => m.ProviderId == xiaomiCoding.Id)
            .ToListAsync();
        Assert.Equal("DeepSeekOpenAI", xiaomiCoding.ProviderType);
        Assert.All(xiaomiCodingModels, model => Assert.Equal("DeepSeekOpenAI", model.ProviderType));
    }

    [Fact]
    public async Task EnsureBuiltInProvidersAsync_DoesNotOverwriteUserOperationalFields()
    {
        await using var context = CreateContext();
        var seeder = CreateSeeder(context);

        await seeder.EnsureBuiltInProvidersAsync();

        var provider = await context.AiProviderConfigs.SingleAsync(p => p.Name == "openai");
        provider.ApiKey = "user-secret";
        provider.BaseUrl = "https://proxy.example.com/v1";
        provider.IsActive = true;

        var model = await context.AiModelConfigs.SingleAsync(m =>
            m.ProviderId == provider.Id && m.ModelId == "gpt-5.2");
        model.IsActive = false;
        model.ProviderType = "OpenAI";
        await context.SaveChangesAsync();

        await seeder.EnsureBuiltInProvidersAsync();

        provider = await context.AiProviderConfigs.SingleAsync(p => p.Name == "openai");
        model = await context.AiModelConfigs.SingleAsync(m =>
            m.ProviderId == provider.Id && m.ModelId == "gpt-5.2");

        Assert.Equal("user-secret", provider.ApiKey);
        Assert.Equal("https://proxy.example.com/v1", provider.BaseUrl);
        Assert.True(provider.IsActive);
        Assert.False(model.IsActive);
        Assert.Equal("OpenAI", model.ProviderType);
    }

    [Fact]
    public async Task MigrateAsync_ReusesMatchingBuiltInProvider()
    {
        await using var context = CreateContext();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Endpoint"] = "https://api.openai.com/v1",
                ["AI:ApiKey"] = "legacy-secret",
                ["AI:RequestType"] = "OpenAI",
                ["WIKI_CONTENT_MODEL"] = "gpt-5.2"
            })
            .Build();
        var seeder = CreateSeeder(context);
        var migration = new AiConfigurationMigrationService(
            context,
            configuration,
            seeder,
            NullLogger<AiConfigurationMigrationService>.Instance);

        await migration.MigrateAsync();

        var provider = await context.AiProviderConfigs.SingleAsync(p => p.Name == "openai");
        Assert.Equal("legacy-secret", provider.ApiKey);
        Assert.Empty(await context.AiProviderConfigs
            .Where(p => p.Name.StartsWith("legacy-wiki") && !p.IsDeleted)
            .ToListAsync());

        var contentProviderSetting = await context.SystemSettings
            .SingleAsync(s => s.Key == SystemSettingDefaults.WikiContentProviderId);
        var contentModelSetting = await context.SystemSettings
            .SingleAsync(s => s.Key == SystemSettingDefaults.WikiContentModelId);
        Assert.Equal(provider.Id, contentProviderSetting.Value);
        Assert.Equal("gpt-5.2", contentModelSetting.Value);
    }

    [Fact]
    public async Task MigrateAsync_DeduplicatesLegacyBindingsWithSameEndpoint()
    {
        await using var context = CreateContext();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Endpoint"] = "https://proxy.example.com/v1",
                ["AI:ApiKey"] = "shared-secret",
                ["AI:RequestType"] = "OpenAI",
                ["WIKI_CATALOG_MODEL"] = "catalog-model",
                ["WIKI_CONTENT_MODEL"] = "content-model",
                ["WIKI_TRANSLATION_MODEL"] = "translation-model",
                ["GRAPHIFY_MODEL"] = "graphify-model"
            })
            .Build();
        var migration = new AiConfigurationMigrationService(
            context,
            configuration,
            CreateSeeder(context),
            NullLogger<AiConfigurationMigrationService>.Instance);

        await migration.MigrateAsync();

        var legacyProviders = await context.AiProviderConfigs
            .Where(p => !p.IsDeleted && p.Name.StartsWith("legacy-"))
            .ToListAsync();
        var provider = Assert.Single(legacyProviders);
        Assert.Equal("https://proxy.example.com/v1", provider.BaseUrl);
        Assert.Equal("shared-secret", provider.ApiKey);
        Assert.DoesNotContain("Wiki", provider.DisplayName ?? string.Empty);
        Assert.DoesNotContain("Graphify", provider.DisplayName ?? string.Empty);

        var providerSettings = await context.SystemSettings
            .Where(s => s.Key == SystemSettingDefaults.WikiCatalogProviderId ||
                        s.Key == SystemSettingDefaults.WikiContentProviderId ||
                        s.Key == SystemSettingDefaults.WikiTranslationProviderId ||
                        s.Key == SystemSettingDefaults.GraphifyProviderId)
            .Select(s => s.Value)
            .ToListAsync();
        Assert.Equal(4, providerSettings.Count);
        Assert.All(providerSettings, value => Assert.Equal(provider.Id, value));
    }

    [Fact]
    public async Task MigrateAsync_RepairsTaskScopedLegacyProviders()
    {
        await using var context = CreateContext();
        var endpoint = "https://proxy.example.com/v1";
        var apiKey = "shared-secret";
        var oldProviders = new[]
        {
            CreateTaskScopedProvider("legacy-wiki-catalog-openai-proxy", "Wiki Catalog Provider", endpoint, apiKey),
            CreateTaskScopedProvider("legacy-wiki-content-openai-proxy", "Wiki Content Provider", endpoint, apiKey),
            CreateTaskScopedProvider("legacy-wiki-translation-openai-proxy", "Wiki Translation Provider", endpoint, apiKey),
            CreateTaskScopedProvider("legacy-graphify-openai-proxy", "Graphify Provider", endpoint, apiKey)
        };
        context.AiProviderConfigs.AddRange(oldProviders);
        context.SystemSettings.AddRange(
            CreateSetting(SystemSettingDefaults.WikiCatalogProviderId, oldProviders[0].Id),
            CreateSetting(SystemSettingDefaults.WikiContentProviderId, oldProviders[1].Id),
            CreateSetting(SystemSettingDefaults.WikiTranslationProviderId, oldProviders[2].Id),
            CreateSetting(SystemSettingDefaults.GraphifyProviderId, oldProviders[3].Id));
        await context.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Endpoint"] = endpoint,
                ["AI:ApiKey"] = apiKey,
                ["AI:RequestType"] = "OpenAI",
                ["WIKI_CATALOG_MODEL"] = "catalog-model",
                ["WIKI_CONTENT_MODEL"] = "content-model",
                ["WIKI_TRANSLATION_MODEL"] = "translation-model",
                ["GRAPHIFY_MODEL"] = "graphify-model"
            })
            .Build();
        var migration = new AiConfigurationMigrationService(
            context,
            configuration,
            CreateSeeder(context),
            NullLogger<AiConfigurationMigrationService>.Instance);

        await migration.MigrateAsync();

        var activeProviders = await context.AiProviderConfigs
            .Where(p => !p.IsDeleted)
            .ToListAsync();
        Assert.DoesNotContain(activeProviders, p =>
            p.Name.StartsWith("legacy-wiki-", StringComparison.OrdinalIgnoreCase) ||
            p.Name.StartsWith("legacy-graphify-", StringComparison.OrdinalIgnoreCase) ||
            p.DisplayName is "Wiki Catalog Provider" or "Wiki Content Provider" or "Wiki Translation Provider" or "Graphify Provider");

        var providerSettings = await context.SystemSettings
            .Where(s => s.Key == SystemSettingDefaults.WikiCatalogProviderId ||
                        s.Key == SystemSettingDefaults.WikiContentProviderId ||
                        s.Key == SystemSettingDefaults.WikiTranslationProviderId ||
                        s.Key == SystemSettingDefaults.GraphifyProviderId)
            .Select(s => s.Value)
            .ToListAsync();
        var targetProviderId = Assert.Single(providerSettings.Distinct());
        Assert.DoesNotContain(oldProviders.Select(p => p.Id).Skip(1), id => id == targetProviderId);
    }

    [Fact]
    public async Task MigrateAsync_RepairsStaleModelBindingsButKeepsValidOnes()
    {
        await using var context = CreateContext();
        var endpoint = "https://proxy.example.com/v1";
        var apiKey = "shared-secret";

        var provider = new AiProviderConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = "legacy-openai-proxy",
            DisplayName = "Migrated OpenAI (proxy.example.com)",
            ProviderType = "OpenAI",
            BaseUrl = endpoint,
            ApiKey = apiKey,
            AuthType = "ApiKey",
            IsActive = true,
            SupportsModelDiscovery = true,
            CreatedAt = DateTime.UtcNow
        };
        context.AiProviderConfigs.Add(provider);

        context.AiModelConfigs.Add(new AiModelConfig
        {
            Id = Guid.NewGuid().ToString(),
            ProviderId = provider.Id,
            ModelId = "valid-model",
            Name = "Valid Model",
            ModelType = "chat",
            ProviderType = "OpenAI",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        context.SystemSettings.AddRange(
            CreateSetting(SystemSettingDefaults.WikiCatalogProviderId, provider.Id),
            CreateSetting(SystemSettingDefaults.WikiCatalogModelId, "stale-model"),
            CreateSetting(SystemSettingDefaults.WikiContentProviderId, provider.Id),
            CreateSetting(SystemSettingDefaults.WikiContentModelId, "valid-model"));
        await context.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Endpoint"] = endpoint,
                ["AI:ApiKey"] = apiKey,
                ["AI:RequestType"] = "OpenAI",
                ["WIKI_CATALOG_MODEL"] = "deepseek-v4-flash",
                ["WIKI_CONTENT_MODEL"] = "deepseek-v4-flash"
            })
            .Build();
        var migration = new AiConfigurationMigrationService(
            context,
            configuration,
            CreateSeeder(context),
            NullLogger<AiConfigurationMigrationService>.Instance);

        await migration.MigrateAsync();

        var catalogModelSetting = await context.SystemSettings
            .SingleAsync(s => s.Key == SystemSettingDefaults.WikiCatalogModelId);
        var contentModelSetting = await context.SystemSettings
            .SingleAsync(s => s.Key == SystemSettingDefaults.WikiContentModelId);

        Assert.Equal("deepseek-v4-flash", catalogModelSetting.Value);
        Assert.Equal("valid-model", contentModelSetting.Value);
        Assert.NotNull(await context.AiModelConfigs.SingleOrDefaultAsync(m =>
            m.ProviderId == provider.Id &&
            m.ModelId == "deepseek-v4-flash" &&
            m.IsActive &&
            !m.IsDeleted));
    }

    private static AiProviderPresetSeeder CreateSeeder(IContext context)
    {
        return new AiProviderPresetSeeder(
            context,
            new AiProviderPresetCatalog(),
            NullLogger<AiProviderPresetSeeder>.Instance);
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new TestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options)
        : MasterDbContext(options);

    private static AiProviderConfig CreateTaskScopedProvider(
        string name,
        string displayName,
        string endpoint,
        string apiKey)
    {
        return new AiProviderConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            DisplayName = displayName,
            ProviderType = "OpenAI",
            BaseUrl = endpoint,
            ApiKey = apiKey,
            AuthType = "ApiKey",
            IsActive = true,
            SupportsModelDiscovery = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static SystemSetting CreateSetting(string key, string value)
    {
        return new SystemSetting
        {
            Id = Guid.NewGuid().ToString(),
            Key = key,
            Value = value,
            Category = "ai",
            CreatedAt = DateTime.UtcNow
        };
    }
}
