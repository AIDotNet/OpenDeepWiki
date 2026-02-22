using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenDeepWiki.Agents;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Chat;
using OpenDeepWiki.Services.Repositories;

namespace OpenDeepWiki.Tests.Services.Chat;

/// <summary>
/// Property-based tests for EmbedService app configuration application.
/// Feature: doc-chat-assistant, Property 12: 应用配置应用正确性
/// Validates: Requirements 13.5
/// </summary>
public class EmbedServiceAppConfigPropertyTests
{
    private static readonly ILogger<ChatAppService> ChatAppLogger = NullLogger<ChatAppService>.Instance;
    private static readonly ILogger<EmbedService> EmbedLogger = NullLogger<EmbedService>.Instance;
    private static readonly ILogger<AppStatisticsService> StatsLogger = NullLogger<AppStatisticsService>.Instance;
    private static readonly ILogger<ChatLogService> LogLogger = NullLogger<ChatLogService>.Instance;
    private static readonly IOptions<RepositoryAnalyzerOptions> RepoOptions = Options.Create(new RepositoryAnalyzerOptions());

    /// <summary>
    /// Creates an in-memory database context for testing.
    /// </summary>
    private static AppConfigTestDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppConfigTestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppConfigTestDbContext(options);
    }

    /// <summary>
    /// Property 12: 应用配置应用正确性 - GetAppConfigAsync应该返回正确的应用名称
    /// For any valid app, GetAppConfigAsync should return the correct app name.
    /// Validates: Requirements 13.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetAppConfig_ShouldReturnCorrectAppName()
    {
        return Prop.ForAll(
            AppConfigGenerators.AppNameArb(),
            AppConfigGenerators.ApiKeyArb(),
            (name, apiKey) =>
            {
                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                // Create app
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = name,
                    ProviderType = "OpenAI",
                    ApiKey = apiKey,
                    AvailableModels = new List<string> { "gpt-4o-mini" },
                    DefaultModel = "gpt-4o-mini"
                }).GetAwaiter().GetResult();

                // Get config
                var config = embedService.GetAppConfigAsync(app.AppId, null)
                    .GetAwaiter().GetResult();

                return (config.Valid && config.AppName == name)
                    .Label($"App name should be '{name}', got '{config.AppName}'");
            });
    }

    /// <summary>
    /// Property 12: 应用配置应用正确性 - GetAppConfigAsync应该返回正确的可用模型列表
    /// For any valid app, GetAppConfigAsync should return the correct available models.
    /// Validates: Requirements 13.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetAppConfig_ShouldReturnCorrectAvailableModels()
    {
        return Prop.ForAll(
            AppConfigGenerators.ModelListArb(),
            AppConfigGenerators.ApiKeyArb(),
            (models, apiKey) =>
            {
                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                // Create app with specific models
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = "TestApp",
                    ProviderType = "OpenAI",
                    ApiKey = apiKey,
                    AvailableModels = models,
                    DefaultModel = models.FirstOrDefault()
                }).GetAwaiter().GetResult();

                // Get config
                var config = embedService.GetAppConfigAsync(app.AppId, null)
                    .GetAwaiter().GetResult();

                // Verify models match
                var modelsMatch = config.Valid &&
                    config.AvailableModels.Count == models.Count &&
                    config.AvailableModels.All(m => models.Contains(m));

                return modelsMatch.Label($"Available models should match configured models");
            });
    }

    /// <summary>
    /// Property 12: 应用配置应用正确性 - GetAppConfigAsync应该返回正确的默认模型
    /// For any valid app, GetAppConfigAsync should return the correct default model.
    /// Validates: Requirements 13.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetAppConfig_ShouldReturnCorrectDefaultModel()
    {
        return Prop.ForAll(
            AppConfigGenerators.ModelArb(),
            AppConfigGenerators.ApiKeyArb(),
            (defaultModel, apiKey) =>
            {
                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                // Create app with specific default model
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = "TestApp",
                    ProviderType = "OpenAI",
                    ApiKey = apiKey,
                    AvailableModels = new List<string> { defaultModel },
                    DefaultModel = defaultModel
                }).GetAwaiter().GetResult();

                // Get config
                var config = embedService.GetAppConfigAsync(app.AppId, null)
                    .GetAwaiter().GetResult();

                return (config.Valid && config.DefaultModel == defaultModel)
                    .Label($"Default model should be '{defaultModel}', got '{config.DefaultModel}'");
            });
    }


    /// <summary>
    /// Property 12: 应用配置应用正确性 - GetAppConfigAsync应该返回正确的图标URL
    /// For any valid app with icon, GetAppConfigAsync should return the correct icon URL.
    /// Validates: Requirements 13.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetAppConfig_ShouldReturnCorrectIconUrl()
    {
        return Prop.ForAll(
            AppConfigGenerators.IconUrlArb(),
            AppConfigGenerators.ApiKeyArb(),
            (iconUrl, apiKey) =>
            {
                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                // Create app with icon
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = "TestApp",
                    IconUrl = iconUrl,
                    ProviderType = "OpenAI",
                    ApiKey = apiKey,
                    AvailableModels = new List<string> { "gpt-4o-mini" },
                    DefaultModel = "gpt-4o-mini"
                }).GetAwaiter().GetResult();

                // Get config
                var config = embedService.GetAppConfigAsync(app.AppId, null)
                    .GetAwaiter().GetResult();

                return (config.Valid && config.IconUrl == iconUrl)
                    .Label($"Icon URL should be '{iconUrl}', got '{config.IconUrl}'");
            });
    }

    /// <summary>
    /// Property 12: 应用配置应用正确性 - 不同ProviderType应该被正确保存
    /// For any valid provider type, the app should be created with correct provider.
    /// Validates: Requirements 13.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CreateApp_ShouldPreserveProviderType()
    {
        return Prop.ForAll(
            AppConfigGenerators.ProviderTypeArb(),
            AppConfigGenerators.ApiKeyArb(),
            (providerType, apiKey) =>
            {
                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);

                // Create app with specific provider type
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = "TestApp",
                    ProviderType = providerType,
                    ApiKey = apiKey,
                    AvailableModels = new List<string> { "gpt-4o-mini" },
                    DefaultModel = "gpt-4o-mini"
                }).GetAwaiter().GetResult();

                return (app.ProviderType == providerType)
                    .Label($"Provider type should be '{providerType}', got '{app.ProviderType}'");
            });
    }

    /// <summary>
    /// Property 12: 应用配置应用正确性 - 更新应用配置应该正确反映
    /// For any app update, the changes should be correctly reflected.
    /// Validates: Requirements 13.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UpdateApp_ShouldReflectChanges()
    {
        return Prop.ForAll(
            AppConfigGenerators.AppNameArb(),
            AppConfigGenerators.AppNameArb(),
            AppConfigGenerators.ApiKeyArb(),
            (originalName, newName, apiKey) =>
            {
                // Skip if names are the same
                if (originalName == newName)
                {
                    return true.Label("Skipped - same name");
                }

                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                // Create app
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = originalName,
                    ProviderType = "OpenAI",
                    ApiKey = apiKey,
                    AvailableModels = new List<string> { "gpt-4o-mini" },
                    DefaultModel = "gpt-4o-mini"
                }).GetAwaiter().GetResult();

                // Update app name
                chatAppService.UpdateAppAsync(app.Id, "user1", new UpdateChatAppDto
                {
                    Name = newName
                }).GetAwaiter().GetResult();

                // Get config
                var config = embedService.GetAppConfigAsync(app.AppId, null)
                    .GetAwaiter().GetResult();

                return (config.Valid && config.AppName == newName)
                    .Label($"Updated app name should be '{newName}', got '{config.AppName}'");
            });
    }


    /// <summary>
    /// Property 12: 应用配置应用正确性 - 域名校验配置应该正确应用
    /// For any app with domain validation, the config should reflect the setting.
    /// Validates: Requirements 13.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetAppConfig_WithDomainValidation_ShouldValidateCorrectly()
    {
        return Prop.ForAll(
            DomainGenerators.BaseDomainArb(),
            AppConfigGenerators.ApiKeyArb(),
            (allowedDomain, apiKey) =>
            {
                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                // Create app with domain validation
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = "TestApp",
                    ProviderType = "OpenAI",
                    ApiKey = apiKey,
                    AvailableModels = new List<string> { "gpt-4o-mini" },
                    DefaultModel = "gpt-4o-mini",
                    EnableDomainValidation = true,
                    AllowedDomains = new List<string> { allowedDomain }
                }).GetAwaiter().GetResult();

                // Get config with allowed domain
                var configAllowed = embedService.GetAppConfigAsync(app.AppId, allowedDomain)
                    .GetAwaiter().GetResult();

                // Get config with disallowed domain
                var configDisallowed = embedService.GetAppConfigAsync(app.AppId, "other-domain.com")
                    .GetAwaiter().GetResult();

                return (configAllowed.Valid && !configDisallowed.Valid)
                    .Label($"Config should be valid for '{allowedDomain}' but invalid for 'other-domain.com'");
            });
    }
}


/// <summary>
/// Generators for app configuration test data.
/// </summary>
public static class AppConfigGenerators
{
    private static readonly string[] AppNames = { "TestApp", "MyApp", "ChatBot", "Assistant", "Helper", "DocBot" };
    private static readonly string[] ApiKeys = { "sk-test-key-123", "sk-prod-key-456", "api-key-789", "sk-demo-000" };
    private static readonly string[] Models = { "gpt-4o-mini", "gpt-4o", "gpt-3.5-turbo", "claude-3-sonnet", "claude-3-haiku" };
    private static readonly string[] ProviderTypes = { "OpenAI", "OpenAIResponses", "Anthropic" };
    private static readonly string?[] IconUrls = { 
        "https://example.com/icon.png", 
        "https://cdn.example.com/bot.svg",
        "https://assets.example.com/chat-icon.png",
        null 
    };

    /// <summary>
    /// Generates valid app names.
    /// </summary>
    public static Arbitrary<string> AppNameArb()
    {
        return Gen.Elements(AppNames).ToArbitrary();
    }

    /// <summary>
    /// Generates valid API keys.
    /// </summary>
    public static Arbitrary<string> ApiKeyArb()
    {
        return Gen.Elements(ApiKeys).ToArbitrary();
    }

    /// <summary>
    /// Generates valid model names.
    /// </summary>
    public static Arbitrary<string> ModelArb()
    {
        return Gen.Elements(Models).ToArbitrary();
    }

    /// <summary>
    /// Generates valid model lists.
    /// </summary>
    public static Arbitrary<List<string>> ModelListArb()
    {
        return Gen.Choose(1, 3)
            .SelectMany(count => Gen.ListOf<string>(Gen.Elements(Models), count))
            .Select(models => models.Distinct().ToList())
            .ToArbitrary();
    }

    /// <summary>
    /// Generates valid provider types.
    /// </summary>
    public static Arbitrary<string> ProviderTypeArb()
    {
        return Gen.Elements(ProviderTypes).ToArbitrary();
    }

    /// <summary>
    /// Generates valid icon URLs (including null).
    /// </summary>
    public static Arbitrary<string?> IconUrlArb()
    {
        return Gen.Elements(IconUrls).ToArbitrary();
    }
}

/// <summary>
/// Test database context for app config tests.
/// </summary>
public class AppConfigTestDbContext : MasterDbContext
{
    public AppConfigTestDbContext(DbContextOptions options) : base(options)
    {
    }
}
