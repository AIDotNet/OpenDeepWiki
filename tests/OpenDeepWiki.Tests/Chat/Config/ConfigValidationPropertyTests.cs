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
/// Property-based tests for config validation completeness.
/// Feature: multi-platform-agent-chat, Property 19: 配置验证完整性
/// Validates: Requirements 11.5
/// </summary>
public class ConfigValidationPropertyTests
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
            "custom"
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
        ).Where(s => !string.IsNullOrEmpty(s));
    }
    
    /// <summary>
    /// 生成完整的飞书配置数据
    /// </summary>
    private static Gen<string> GenerateCompleteFeishuConfigData()
    {
        return Gen.Constant("""{"AppId": "app123", "AppSecret": "secret456"}""");
    }
    
    /// <summary>
    /// 生成完整的 QQ 配置数据
    /// </summary>
    private static Gen<string> GenerateCompleteQQConfigData()
    {
        return Gen.Constant("""{"AppId": "app123", "Token": "token456"}""");
    }
    
    /// <summary>
    /// 生成完整的微信配置数据
    /// </summary>
    private static Gen<string> GenerateCompleteWeChatConfigData()
    {
        return Gen.Constant("""{"AppId": "app123", "AppSecret": "secret456", "Token": "token789", "EncodingAesKey": "aeskey123"}""");
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
    /// Property 19: 配置验证完整性
    /// For any 缺少 Platform 的配置，验证应失败并返回明确的错误信息。
    /// Validates: Requirements 11.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigValidation_MissingPlatform_ShouldFail()
    {
        return Prop.ForAll(
            GenerateValidDisplayName().ToArbitrary(),
            displayName =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = string.Empty, // 缺少 Platform
                    DisplayName = displayName,
                    IsEnabled = true,
                    ConfigData = """{"ApiKey": "test"}""",
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                var result = service.ValidateConfig(config);
                
                // 验证应失败，且错误信息应包含 Platform
                return !result.IsValid && 
                       result.MissingFields.Contains("Platform");
            });
    }
    
    /// <summary>
    /// Property 19: 配置验证完整性
    /// For any 缺少 DisplayName 的配置，验证应失败并返回明确的错误信息。
    /// Validates: Requirements 11.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigValidation_MissingDisplayName_ShouldFail()
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
                    DisplayName = string.Empty, // 缺少 DisplayName
                    IsEnabled = true,
                    ConfigData = """{"ApiKey": "test"}""",
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                var result = service.ValidateConfig(config);
                
                // 验证应失败，且错误信息应包含 DisplayName
                return !result.IsValid && 
                       result.MissingFields.Contains("DisplayName");
            });
    }
    
    /// <summary>
    /// Property 19: 配置验证完整性
    /// For any 负数的 MessageInterval，验证应失败。
    /// Validates: Requirements 11.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigValidation_NegativeMessageInterval_ShouldFail()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            GenerateValidDisplayName().ToArbitrary(),
            Gen.Choose(-1000, -1).ToArbitrary(),
            (platform, displayName, messageInterval) =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = platform,
                    DisplayName = displayName,
                    IsEnabled = true,
                    ConfigData = """{"ApiKey": "test"}""",
                    MessageInterval = messageInterval, // 负数
                    MaxRetryCount = 3
                };
                
                var result = service.ValidateConfig(config);
                
                // 验证应失败
                return !result.IsValid && 
                       result.Errors.Any(e => e.Contains("MessageInterval"));
            });
    }
    
    /// <summary>
    /// Property 19: 配置验证完整性
    /// For any 负数的 MaxRetryCount，验证应失败。
    /// Validates: Requirements 11.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigValidation_NegativeMaxRetryCount_ShouldFail()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            GenerateValidDisplayName().ToArbitrary(),
            Gen.Choose(-100, -1).ToArbitrary(),
            (platform, displayName, maxRetryCount) =>
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
                    MaxRetryCount = maxRetryCount // 负数
                };
                
                var result = service.ValidateConfig(config);
                
                // 验证应失败
                return !result.IsValid && 
                       result.Errors.Any(e => e.Contains("MaxRetryCount"));
            });
    }
    
    /// <summary>
    /// Property 19: 配置验证完整性
    /// For any 飞书平台缺少 AppId 的配置，验证应失败并指出缺少的字段。
    /// Validates: Requirements 11.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigValidation_FeishuMissingAppId_ShouldFail()
    {
        return Prop.ForAll(
            GenerateValidDisplayName().ToArbitrary(),
            displayName =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = "feishu",
                    DisplayName = displayName,
                    IsEnabled = true,
                    ConfigData = """{"AppSecret": "secret456"}""", // 缺少 AppId
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                var result = service.ValidateConfig(config);
                
                // 验证应失败，且应指出缺少 AppId
                return !result.IsValid && 
                       result.MissingFields.Contains("AppId");
            });
    }
    
    /// <summary>
    /// Property 19: 配置验证完整性
    /// For any 飞书平台缺少 AppSecret 的配置，验证应失败并指出缺少的字段。
    /// Validates: Requirements 11.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigValidation_FeishuMissingAppSecret_ShouldFail()
    {
        return Prop.ForAll(
            GenerateValidDisplayName().ToArbitrary(),
            displayName =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = "feishu",
                    DisplayName = displayName,
                    IsEnabled = true,
                    ConfigData = """{"AppId": "app123"}""", // 缺少 AppSecret
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                var result = service.ValidateConfig(config);
                
                // 验证应失败，且应指出缺少 AppSecret
                return !result.IsValid && 
                       result.MissingFields.Contains("AppSecret");
            });
    }
    
    /// <summary>
    /// Property 19: 配置验证完整性
    /// For any 完整的飞书配置，验证应通过。
    /// Validates: Requirements 11.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigValidation_CompleteFeishuConfig_ShouldPass()
    {
        return Prop.ForAll(
            GenerateValidDisplayName().ToArbitrary(),
            GenerateCompleteFeishuConfigData().ToArbitrary(),
            (displayName, configData) =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = "feishu",
                    DisplayName = displayName,
                    IsEnabled = true,
                    ConfigData = configData,
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                var result = service.ValidateConfig(config);
                
                // 验证应通过
                return result.IsValid;
            });
    }
    
    /// <summary>
    /// Property 19: 配置验证完整性
    /// For any QQ 平台缺少必需字段的配置，验证应失败。
    /// Validates: Requirements 11.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigValidation_QQMissingRequiredFields_ShouldFail()
    {
        return Prop.ForAll(
            GenerateValidDisplayName().ToArbitrary(),
            displayName =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = "qq",
                    DisplayName = displayName,
                    IsEnabled = true,
                    ConfigData = """{"SomeOtherField": "value"}""", // 缺少 AppId 和 Token
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                var result = service.ValidateConfig(config);
                
                // 验证应失败
                return !result.IsValid && 
                       (result.MissingFields.Contains("AppId") || result.MissingFields.Contains("Token"));
            });
    }
    
    /// <summary>
    /// Property 19: 配置验证完整性
    /// For any 完整的 QQ 配置，验证应通过。
    /// Validates: Requirements 11.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigValidation_CompleteQQConfig_ShouldPass()
    {
        return Prop.ForAll(
            GenerateValidDisplayName().ToArbitrary(),
            GenerateCompleteQQConfigData().ToArbitrary(),
            (displayName, configData) =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = "qq",
                    DisplayName = displayName,
                    IsEnabled = true,
                    ConfigData = configData,
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                var result = service.ValidateConfig(config);
                
                // 验证应通过
                return result.IsValid;
            });
    }
    
    /// <summary>
    /// Property 19: 配置验证完整性
    /// For any 完整的微信配置，验证应通过。
    /// Validates: Requirements 11.5
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigValidation_CompleteWeChatConfig_ShouldPass()
    {
        return Prop.ForAll(
            GenerateValidDisplayName().ToArbitrary(),
            GenerateCompleteWeChatConfigData().ToArbitrary(),
            (displayName, configData) =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var config = new ProviderConfigDto
                {
                    Platform = "wechat",
                    DisplayName = displayName,
                    IsEnabled = true,
                    ConfigData = configData,
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                
                var result = service.ValidateConfig(config);
                
                // 验证应通过
                return result.IsValid;
            });
    }
    
    /// <summary>
    /// Property 19: 配置验证完整性
    /// For any 自定义平台的配置（无特定必需字段），验证应通过。
    /// Validates: Requirements 11.5
    /// </summary>
    [Fact]
    public void ConfigValidation_CustomPlatformConfig_ShouldPass()
    {
        using var context = TestConfigDbContext.Create();
        var service = CreateConfigService(context);
        
        var displayNames = new[] { "飞书机器人", "QQ Bot", "微信客服", "Test Provider", "Custom Platform" };
        
        foreach (var displayName in displayNames)
        {
            var config = new ProviderConfigDto
            {
                Platform = "custom",
                DisplayName = displayName,
                IsEnabled = true,
                ConfigData = """{"AnyField": "anyValue"}""",
                MessageInterval = 500,
                MaxRetryCount = 3
            };
            
            var result = service.ValidateConfig(config);
            
            // 自定义平台没有特定必需字段，应通过
            Assert.True(result.IsValid, $"Config with DisplayName '{displayName}' should be valid");
        }
    }
    
    /// <summary>
    /// Property 19: 配置验证完整性
    /// ValidateAllConfigs 应验证所有已保存的配置。
    /// Validates: Requirements 11.5
    /// </summary>
    [Fact]
    public async Task ConfigValidation_ValidateAllConfigs_ShouldValidateAllSavedConfigs()
    {
        using var context = TestConfigDbContext.Create();
        var service = CreateConfigService(context);
        
        // 保存多个配置
        await service.SaveConfigAsync(new ProviderConfigDto
        {
            Platform = "feishu",
            DisplayName = "飞书",
            IsEnabled = true,
            ConfigData = """{"AppId": "app1", "AppSecret": "secret1"}""",
            MessageInterval = 500,
            MaxRetryCount = 3
        });
        
        await service.SaveConfigAsync(new ProviderConfigDto
        {
            Platform = "qq",
            DisplayName = "QQ",
            IsEnabled = true,
            ConfigData = """{"AppId": "app2", "Token": "token2"}""",
            MessageInterval = 500,
            MaxRetryCount = 3
        });
        
        // 验证所有配置
        var results = (await service.ValidateAllConfigsAsync()).ToList();
        
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.IsValid));
    }
    
    /// <summary>
    /// Property 19: 配置验证完整性
    /// ConfigValidator 静态方法应正确验证配置。
    /// Validates: Requirements 11.5
    /// </summary>
    [Fact]
    public void ConfigValidation_StaticValidator_ShouldValidateCorrectly()
    {
        var validConfig = new ProviderConfigDto
        {
            Platform = "feishu",
            DisplayName = "飞书",
            IsEnabled = true,
            ConfigData = """{"AppId": "app1", "AppSecret": "secret1"}""",
            MessageInterval = 500,
            MaxRetryCount = 3
        };
        
        var invalidConfig = new ProviderConfigDto
        {
            Platform = string.Empty,
            DisplayName = string.Empty,
            IsEnabled = true,
            ConfigData = """{"ApiKey": "test"}""",
            MessageInterval = -1,
            MaxRetryCount = -1
        };
        
        var validResult = ConfigValidator.Validate(validConfig);
        var invalidResult = ConfigValidator.Validate(invalidConfig);
        
        Assert.True(validResult.IsValid);
        Assert.False(invalidResult.IsValid);
        Assert.Contains("Platform", invalidResult.MissingFields);
        Assert.Contains("DisplayName", invalidResult.MissingFields);
    }
}
