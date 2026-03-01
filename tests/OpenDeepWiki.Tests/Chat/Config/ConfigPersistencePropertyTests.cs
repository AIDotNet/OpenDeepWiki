using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenDeepWiki.Chat.Config;
using Xunit;

namespace OpenDeepWiki.Tests.Chat.Config;

/// <summary>
/// Property-based tests for config persistence round-trip consistency.
/// Feature: multi-platform-agent-chat, Property 16: 配置持久化往返一致性
/// Validates: Requirements 11.2
/// </summary>
public class ConfigPersistencePropertyTests
{
    /// <summary>
    /// 生成有效的平台标识
    /// </summary>
    private static Gen<string> GenerateValidPlatform()
    {
        return Gen.Elements(
            "feishu",
            "qq",
            "wechat",
            "test",
            "custom",
            "slack",
            "telegram"
        );
    }
    
    /// <summary>
    /// 生成有效的显示名称
    /// </summary>
    private static Gen<string> GenerateValidDisplayName()
    {
        return Gen.Elements(
            "飞书机器人",
            "QQ Bot",
            "微信客服",
            "Test Provider",
            "Custom Platform"
        );
    }
    
    /// <summary>
    /// 生成有效的配置数据 JSON
    /// </summary>
    private static Gen<string> GenerateValidConfigData()
    {
        return Gen.Elements(
            """{"ApiKey": "test-key-123"}""",
            """{"AppId": "app123", "AppSecret": "secret456"}""",
            """{"Token": "token789", "Endpoint": "https://api.example.com"}""",
            """{"ClientId": "client1", "ClientSecret": "secret1"}"""
        );
    }
    
    /// <summary>
    /// 生成有效的 Webhook URL
    /// </summary>
    private static Gen<string?> GenerateValidWebhookUrl()
    {
        return Gen.Elements<string?>(
            null,
            "https://example.com/webhook",
            "https://api.example.com/callback",
            "http://localhost:8080/hook"
        );
    }
    
    /// <summary>
    /// 创建测试用的 ChatConfigService
    /// </summary>
    private static ChatConfigService CreateConfigService(TestConfigDbContext context)
    {
        var logger = new LoggerFactory().CreateLogger<ChatConfigService>();
        var encryptionOptions = Options.Create(new ConfigEncryptionOptions
        {
            EncryptionKey = "test-encryption-key-for-testing"
        });
        var encryption = new AesConfigEncryption(encryptionOptions);
        var changeNotifier = new Mock<IConfigChangeNotifier>().Object;
        
        return new ChatConfigService(context, encryption, changeNotifier, logger);
    }

    /// <summary>
    /// Property 16: 配置持久化往返一致性
    /// For any 有效的 ProviderConfig，保存到数据库后再加载，Platform 应保持一致。
    /// Validates: Requirements 11.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigPersistence_Platform_ShouldBeConsistent()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            GenerateValidDisplayName().ToArbitrary(),
            (platform, displayName) =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = platform,
                    DisplayName = displayName,
                    IsEnabled = true,
                    ConfigData = """{"ApiKey": "test"}""",
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                // 保存配置
                service.SaveConfigAsync(config).Wait();
                
                // 重新加载
                var loaded = service.GetConfigAsync(platform).Result;
                
                return loaded != null && loaded.Platform == platform;
            });
    }
    
    /// <summary>
    /// Property 16: 配置持久化往返一致性
    /// For any 有效的 ProviderConfig，保存到数据库后再加载，DisplayName 应保持一致。
    /// Validates: Requirements 11.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigPersistence_DisplayName_ShouldBeConsistent()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            GenerateValidDisplayName().ToArbitrary(),
            (platform, displayName) =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = platform,
                    DisplayName = displayName,
                    IsEnabled = true,
                    ConfigData = """{"ApiKey": "test"}""",
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                service.SaveConfigAsync(config).Wait();
                var loaded = service.GetConfigAsync(platform).Result;
                
                return loaded != null && loaded.DisplayName == displayName;
            });
    }
    
    /// <summary>
    /// Property 16: 配置持久化往返一致性
    /// For any 有效的 ProviderConfig，保存到数据库后再加载，IsEnabled 应保持一致。
    /// Validates: Requirements 11.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigPersistence_IsEnabled_ShouldBeConsistent()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            Gen.Elements(true, false).ToArbitrary(),
            (platform, isEnabled) =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = platform,
                    DisplayName = "Test",
                    IsEnabled = isEnabled,
                    ConfigData = """{"ApiKey": "test"}""",
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                service.SaveConfigAsync(config).Wait();
                var loaded = service.GetConfigAsync(platform).Result;
                
                return loaded != null && loaded.IsEnabled == isEnabled;
            });
    }
    
    /// <summary>
    /// Property 16: 配置持久化往返一致性
    /// For any 有效的 ProviderConfig，保存到数据库后再加载，MessageInterval 应保持一致。
    /// Validates: Requirements 11.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigPersistence_MessageInterval_ShouldBeConsistent()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            Gen.Choose(0, 5000).ToArbitrary(),
            (platform, messageInterval) =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = platform,
                    DisplayName = "Test",
                    IsEnabled = true,
                    ConfigData = """{"ApiKey": "test"}""",
                    MessageInterval = messageInterval,
                    MaxRetryCount = 3
                };
                
                service.SaveConfigAsync(config).Wait();
                var loaded = service.GetConfigAsync(platform).Result;
                
                return loaded != null && loaded.MessageInterval == messageInterval;
            });
    }
    
    /// <summary>
    /// Property 16: 配置持久化往返一致性
    /// For any 有效的 ProviderConfig，保存到数据库后再加载，MaxRetryCount 应保持一致。
    /// Validates: Requirements 11.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigPersistence_MaxRetryCount_ShouldBeConsistent()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            Gen.Choose(0, 10).ToArbitrary(),
            (platform, maxRetryCount) =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = platform,
                    DisplayName = "Test",
                    IsEnabled = true,
                    ConfigData = """{"ApiKey": "test"}""",
                    MessageInterval = 500,
                    MaxRetryCount = maxRetryCount
                };
                
                service.SaveConfigAsync(config).Wait();
                var loaded = service.GetConfigAsync(platform).Result;
                
                return loaded != null && loaded.MaxRetryCount == maxRetryCount;
            });
    }
    
    /// <summary>
    /// Property 16: 配置持久化往返一致性
    /// For any 有效的 ProviderConfig，保存到数据库后再加载，WebhookUrl 应保持一致。
    /// Validates: Requirements 11.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigPersistence_WebhookUrl_ShouldBeConsistent()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            GenerateValidWebhookUrl().ToArbitrary(),
            (platform, webhookUrl) =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = platform,
                    DisplayName = "Test",
                    IsEnabled = true,
                    ConfigData = """{"ApiKey": "test"}""",
                    WebhookUrl = webhookUrl,
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                service.SaveConfigAsync(config).Wait();
                var loaded = service.GetConfigAsync(platform).Result;
                
                return loaded != null && loaded.WebhookUrl == webhookUrl;
            });
    }
    
    /// <summary>
    /// Property 16: 配置持久化往返一致性
    /// For any 有效的 ProviderConfig，GetAllConfigs 应包含已保存的配置。
    /// Validates: Requirements 11.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigPersistence_GetAllConfigs_ShouldIncludeSavedConfig()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            GenerateValidDisplayName().ToArbitrary(),
            (platform, displayName) =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = platform,
                    DisplayName = displayName,
                    IsEnabled = true,
                    ConfigData = """{"ApiKey": "test"}""",
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                service.SaveConfigAsync(config).Wait();
                var allConfigs = service.GetAllConfigsAsync().Result.ToList();
                
                return allConfigs.Any(c => c.Platform == platform && c.DisplayName == displayName);
            });
    }
    
    /// <summary>
    /// Property 16: 配置持久化往返一致性
    /// For any 删除的配置，GetConfig 应返回 null。
    /// Validates: Requirements 11.2
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigPersistence_DeletedConfig_ShouldReturnNull()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            platform =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = platform,
                    DisplayName = "Test",
                    IsEnabled = true,
                    ConfigData = """{"ApiKey": "test"}""",
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                service.SaveConfigAsync(config).Wait();
                service.DeleteConfigAsync(platform).Wait();
                var loaded = service.GetConfigAsync(platform).Result;
                
                return loaded == null;
            });
    }
    
    /// <summary>
    /// Property 16: 配置持久化往返一致性
    /// For any 不存在的平台，GetConfig 应返回 null。
    /// Validates: Requirements 11.2
    /// </summary>
    [Fact]
    public async Task ConfigPersistence_NonExistentPlatform_ShouldReturnNull()
    {
        using var context = TestConfigDbContext.Create();
        var service = CreateConfigService(context);
        
        var loaded = await service.GetConfigAsync("non-existent-platform");
        
        Assert.Null(loaded);
    }
}
