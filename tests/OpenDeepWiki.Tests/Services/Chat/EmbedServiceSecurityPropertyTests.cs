using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Chat;
using OpenDeepWiki.Services.Repositories;


namespace OpenDeepWiki.Tests.Services.Chat;

/// <summary>
/// Property-based tests for EmbedService security validation.
/// Feature: doc-chat-assistant, Property 11: 安全验证完整性
/// Validates: Requirements 17.1, 17.2
/// </summary>
public class EmbedServiceSecurityPropertyTests
{
    private static readonly ILogger<ChatAppService> ChatAppLogger = NullLogger<ChatAppService>.Instance;
    private static readonly ILogger<EmbedService> EmbedLogger = NullLogger<EmbedService>.Instance;
    private static readonly ILogger<AppStatisticsService> StatsLogger = NullLogger<AppStatisticsService>.Instance;
    private static readonly ILogger<ChatLogService> LogLogger = NullLogger<ChatLogService>.Instance;
    private static readonly IOptions<RepositoryAnalyzerOptions> RepoOptions = Options.Create(new RepositoryAnalyzerOptions());

    /// <summary>
    /// Creates an in-memory database context for testing.
    /// </summary>
    private static SecurityTestDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<SecurityTestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new SecurityTestDbContext(options);
    }

    /// <summary>
    /// Property 11: 安全验证完整性 - 无效的AppId应该被拒绝
    /// For any invalid AppId, validation should fail.
    /// Validates: Requirements 17.1
    /// </summary>
    [Property(MaxTest = 100)]
    public Property InvalidAppId_ShouldBeRejected()
    {
        return Prop.ForAll(
            SecurityGenerators.InvalidAppIdArb(),
            invalidAppId =>
            {
                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                var (isValid, errorCode, _) = embedService.ValidateAppAsync(invalidAppId)
                    .GetAwaiter().GetResult();

                return (!isValid && errorCode == "INVALID_APP_ID")
                    .Label($"Invalid AppId '{invalidAppId}' should be rejected");
            });
    }

    /// <summary>
    /// Property 11: 安全验证完整性 - 有效的AppId应该被接受
    /// For any valid and active app, validation should succeed.
    /// Validates: Requirements 17.1
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ValidAppId_ShouldBeAccepted()
    {
        return Prop.ForAll(
            SecurityGenerators.ValidAppConfigArb(),
            config =>
            {
                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                // Create a valid app
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = config.Name,
                    ProviderType = "OpenAI",
                    ApiKey = config.ApiKey,
                    AvailableModels = new List<string> { "gpt-4o-mini" },
                    DefaultModel = "gpt-4o-mini"
                }).GetAwaiter().GetResult();

                var (isValid, errorCode, _) = embedService.ValidateAppAsync(app.AppId)
                    .GetAwaiter().GetResult();

                return (isValid && errorCode == null)
                    .Label($"Valid AppId '{app.AppId}' should be accepted");
            });
    }

    /// <summary>
    /// Property 11: 安全验证完整性 - 停用的应用应该被拒绝
    /// For any inactive app, validation should fail.
    /// Validates: Requirements 17.1
    /// </summary>
    [Property(MaxTest = 100)]
    public Property InactiveApp_ShouldBeRejected()
    {
        return Prop.ForAll(
            SecurityGenerators.ValidAppConfigArb(),
            config =>
            {
                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                // Create and then deactivate the app
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = config.Name,
                    ProviderType = "OpenAI",
                    ApiKey = config.ApiKey,
                    AvailableModels = new List<string> { "gpt-4o-mini" }
                }).GetAwaiter().GetResult();

                chatAppService.UpdateAppAsync(app.Id, "user1", new UpdateChatAppDto
                {
                    IsActive = false
                }).GetAwaiter().GetResult();

                var (isValid, errorCode, _) = embedService.ValidateAppAsync(app.AppId)
                    .GetAwaiter().GetResult();

                return (!isValid && errorCode == "APP_INACTIVE")
                    .Label($"Inactive app '{app.AppId}' should be rejected");
            });
    }


    /// <summary>
    /// Property 11: 安全验证完整性 - 未配置API密钥的应用应该被拒绝
    /// For any app without API key, validation should fail.
    /// Validates: Requirements 17.1
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AppWithoutApiKey_ShouldBeRejected()
    {
        return Prop.ForAll(
            SecurityGenerators.AppNameArb(),
            name =>
            {
                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                // Create app without API key
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = name,
                    ProviderType = "OpenAI",
                    ApiKey = null,
                    AvailableModels = new List<string> { "gpt-4o-mini" }
                }).GetAwaiter().GetResult();

                var (isValid, errorCode, _) = embedService.ValidateAppAsync(app.AppId)
                    .GetAwaiter().GetResult();

                return (!isValid && errorCode == "CONFIG_MISSING")
                    .Label($"App without API key should be rejected");
            });
    }


    /// <summary>
    /// Property 11: 安全验证完整性 - 启用域名校验时，非允许域名应该被拒绝
    /// For any app with domain validation enabled, requests from non-allowed domains should be rejected.
    /// Validates: Requirements 17.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DomainValidationEnabled_NonAllowedDomain_ShouldBeRejected()
    {
        return Prop.ForAll(
            SecurityGenerators.ValidAppConfigArb(),
            DomainGenerators.BaseDomainArb(),
            DomainGenerators.DifferentBaseDomainArb(),
            (config, allowedDomain, requestDomain) =>
            {
                // Ensure domains are different
                if (allowedDomain == requestDomain)
                {
                    return true.Label("Skipped - same domain");
                }

                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                // Create app with domain validation enabled
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = config.Name,
                    ProviderType = "OpenAI",
                    ApiKey = config.ApiKey,
                    AvailableModels = new List<string> { "gpt-4o-mini" },
                    EnableDomainValidation = true,
                    AllowedDomains = new List<string> { allowedDomain }
                }).GetAwaiter().GetResult();

                var (isValid, errorCode, _) = embedService.ValidateDomainAsync(app.AppId, requestDomain)
                    .GetAwaiter().GetResult();

                return (!isValid && errorCode == "DOMAIN_NOT_ALLOWED")
                    .Label($"Request from '{requestDomain}' should be rejected when only '{allowedDomain}' is allowed");
            });
    }

    /// <summary>
    /// Property 11: 安全验证完整性 - 启用域名校验时，允许的域名应该被接受
    /// For any app with domain validation enabled, requests from allowed domains should be accepted.
    /// Validates: Requirements 17.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DomainValidationEnabled_AllowedDomain_ShouldBeAccepted()
    {
        return Prop.ForAll(
            SecurityGenerators.ValidAppConfigArb(),
            DomainGenerators.BaseDomainArb(),
            (config, domain) =>
            {
                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                // Create app with domain validation enabled
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = config.Name,
                    ProviderType = "OpenAI",
                    ApiKey = config.ApiKey,
                    AvailableModels = new List<string> { "gpt-4o-mini" },
                    EnableDomainValidation = true,
                    AllowedDomains = new List<string> { domain }
                }).GetAwaiter().GetResult();

                var (isValid, errorCode, _) = embedService.ValidateDomainAsync(app.AppId, domain)
                    .GetAwaiter().GetResult();

                return (isValid && errorCode == null)
                    .Label($"Request from allowed domain '{domain}' should be accepted");
            });
    }

    /// <summary>
    /// Property 11: 安全验证完整性 - 禁用域名校验时，任何域名都应该被接受
    /// For any app with domain validation disabled, requests from any domain should be accepted.
    /// Validates: Requirements 17.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DomainValidationDisabled_AnyDomain_ShouldBeAccepted()
    {
        return Prop.ForAll(
            SecurityGenerators.ValidAppConfigArb(),
            DomainGenerators.ValidDomainArb(),
            (config, domain) =>
            {
                using var context = CreateInMemoryContext();
                var chatAppService = new ChatAppService(context, ChatAppLogger);
                var statsService = new AppStatisticsService(context, StatsLogger);
                var logService = new ChatLogService(context, LogLogger);
                var embedService = new EmbedService(
                    context, null!, chatAppService, statsService, logService, null!, RepoOptions, EmbedLogger);

                // Create app with domain validation disabled
                var app = chatAppService.CreateAppAsync("user1", new CreateChatAppDto
                {
                    Name = config.Name,
                    ProviderType = "OpenAI",
                    ApiKey = config.ApiKey,
                    AvailableModels = new List<string> { "gpt-4o-mini" },
                    EnableDomainValidation = false
                }).GetAwaiter().GetResult();

                var (isValid, errorCode, _) = embedService.ValidateDomainAsync(app.AppId, domain)
                    .GetAwaiter().GetResult();

                return (isValid && errorCode == null)
                    .Label($"Any domain '{domain}' should be accepted when validation is disabled");
            });
    }
}


/// <summary>
/// Generators for security-related test data.
/// </summary>
public static class SecurityGenerators
{
    private static readonly string[] InvalidAppIds = { "", "   ", "invalid", "app_", "app_short", "not_an_app_id" };
    private static readonly string[] AppNames = { "TestApp", "MyApp", "ChatBot", "Assistant", "Helper" };
    private static readonly string[] ApiKeys = { "sk-test-key-123", "sk-prod-key-456", "api-key-789" };

    /// <summary>
    /// Generates invalid AppIds.
    /// </summary>
    public static Arbitrary<string> InvalidAppIdArb()
    {
        return Gen.Elements(InvalidAppIds).ToArbitrary();
    }

    /// <summary>
    /// Generates valid app names.
    /// </summary>
    public static Arbitrary<string> AppNameArb()
    {
        return Gen.Elements(AppNames).ToArbitrary();
    }

    /// <summary>
    /// Generates valid app configurations.
    /// </summary>
    public static Arbitrary<ValidAppConfig> ValidAppConfigArb()
    {
        return Gen.Elements(AppNames)
            .SelectMany(name => Gen.Elements(ApiKeys).Select(apiKey => new ValidAppConfig
            {
                Name = name,
                ApiKey = apiKey
            }))
            .ToArbitrary();
    }
}

/// <summary>
/// Valid app configuration for testing.
/// </summary>
public class ValidAppConfig
{
    public string Name { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Test database context for security tests.
/// </summary>
public class SecurityTestDbContext : MasterDbContext
{
    public SecurityTestDbContext(DbContextOptions options) : base(options)
    {
    }
}
