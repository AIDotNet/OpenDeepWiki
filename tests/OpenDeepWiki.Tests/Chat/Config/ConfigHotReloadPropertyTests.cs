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
/// Property-based tests for config hot reload.
/// Feature: multi-platform-agent-chat, Property 18: 配置热重载
/// Validates: Requirements 11.3
/// </summary>
public class ConfigHotReloadPropertyTests
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
    /// 创建测试用的 ConfigChangeNotifier
    /// </summary>
    private static ConfigChangeNotifier CreateChangeNotifier()
    {
        var logger = new LoggerFactory().CreateLogger<ConfigChangeNotifier>();
        return new ConfigChangeNotifier(logger);
    }

    /// <summary>
    /// Property 18: 配置热重载
    /// For any 配置变更，变更后获取的配置值应反映最新的变更。
    /// Validates: Requirements 11.3
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigHotReload_UpdatedConfig_ShouldReflectChanges()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            GenerateValidDisplayName().ToArbitrary(),
            GenerateValidDisplayName().ToArbitrary(),
            (platform, originalName, updatedName) =>
            {
                if (originalName == updatedName) return true; // 跳过相同值的情况
                
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                // 创建原始配置
                var config = new ProviderConfigDto
                {
                    Platform = platform,
                    DisplayName = originalName,
                    IsEnabled = true,
                    ConfigData = """{"ApiKey": "test"}""",
                    MessageInterval = 500,
                    MaxRetryCount = 3
                };
                service.SaveConfigAsync(config).Wait();
                
                // 更新配置
                config.DisplayName = updatedName;
                service.SaveConfigAsync(config).Wait();
                
                // 获取最新配置
                var loaded = service.GetConfigAsync(platform).Result;
                
                // 应反映最新的变更
                return loaded != null && loaded.DisplayName == updatedName;
            });
    }
    
    /// <summary>
    /// Property 18: 配置热重载
    /// For any 配置变更，OnConfigChanged 回调应被触发。
    /// Validates: Requirements 11.3
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigHotReload_ConfigChange_ShouldTriggerCallback()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            platform =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var callbackTriggered = false;
                var callbackPlatform = string.Empty;
                
                // 注册回调
                using var subscription = service.OnConfigChanged(p =>
                {
                    callbackTriggered = true;
                    callbackPlatform = p;
                });
                
                // 保存配置
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
                
                // 回调应被触发
                return callbackTriggered && callbackPlatform == platform;
            });
    }
    
    /// <summary>
    /// Property 18: 配置热重载
    /// For any 取消订阅后的配置变更，回调不应被触发。
    /// Validates: Requirements 11.3
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigHotReload_UnsubscribedCallback_ShouldNotBeTrigger()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            platform =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                var callbackTriggered = false;
                
                // 注册并立即取消订阅
                var subscription = service.OnConfigChanged(_ => callbackTriggered = true);
                subscription.Dispose();
                
                // 保存配置
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
                
                // 回调不应被触发
                return !callbackTriggered;
            });
    }
    
    /// <summary>
    /// Property 18: 配置热重载
    /// For any 配置删除，OnConfigChanged 回调应被触发。
    /// Validates: Requirements 11.3
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigHotReload_ConfigDelete_ShouldTriggerCallback()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            platform =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                // 先创建配置
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
                
                var deleteCallbackTriggered = false;
                
                // 注册回调
                using var subscription = service.OnConfigChanged(p =>
                {
                    if (p == platform)
                        deleteCallbackTriggered = true;
                });
                
                // 删除配置
                service.DeleteConfigAsync(platform).Wait();
                
                // 回调应被触发
                return deleteCallbackTriggered;
            });
    }
    
    /// <summary>
    /// Property 18: 配置热重载
    /// For any ReloadConfig 调用，OnConfigChanged 回调应被触发。
    /// Validates: Requirements 11.3
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigHotReload_ReloadConfig_ShouldTriggerCallback()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            platform =>
            {
                using var context = TestConfigDbContext.Create();
                var service = CreateConfigService(context);
                
                // 先创建配置
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
                
                var reloadCallbackTriggered = false;
                
                // 注册回调
                using var subscription = service.OnConfigChanged(p =>
                {
                    if (p == platform)
                        reloadCallbackTriggered = true;
                });
                
                // 触发重载
                service.ReloadConfigAsync(platform).Wait();
                
                // 回调应被触发
                return reloadCallbackTriggered;
            });
    }
    
    /// <summary>
    /// Property 18: 配置热重载
    /// ConfigChangeNotifier 应正确通知订阅者。
    /// Validates: Requirements 11.3
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigHotReload_ChangeNotifier_ShouldNotifySubscribers()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            platform =>
            {
                var notifier = CreateChangeNotifier();
                
                var notified = false;
                ConfigChangeEvent? receivedEvent = null;
                
                // 订阅
                using var subscription = notifier.Subscribe(platform, e =>
                {
                    notified = true;
                    receivedEvent = e;
                });
                
                // 通知变更
                notifier.NotifyChange(platform, ConfigChangeType.Updated);
                
                // 应收到通知
                return notified && 
                       receivedEvent != null && 
                       receivedEvent.Platform == platform &&
                       receivedEvent.ChangeType == ConfigChangeType.Updated;
            });
    }
    
    /// <summary>
    /// Property 18: 配置热重载
    /// ConfigChangeNotifier 订阅 null 平台应接收所有平台的通知。
    /// Validates: Requirements 11.3
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigHotReload_ChangeNotifier_NullPlatformSubscription_ShouldReceiveAllNotifications()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            platform =>
            {
                var notifier = CreateChangeNotifier();
                
                var notificationCount = 0;
                
                // 订阅所有平台
                using var subscription = notifier.Subscribe(null, _ => notificationCount++);
                
                // 通知多个平台
                notifier.NotifyChange(platform, ConfigChangeType.Created);
                notifier.NotifyChange("other-platform", ConfigChangeType.Updated);
                
                // 应收到所有通知
                return notificationCount == 2;
            });
    }
    
    /// <summary>
    /// Property 18: 配置热重载
    /// ConfigChangeNotifier 订阅特定平台不应接收其他平台的通知。
    /// Validates: Requirements 11.3
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ConfigHotReload_ChangeNotifier_SpecificPlatformSubscription_ShouldNotReceiveOtherNotifications()
    {
        return Prop.ForAll(
            GenerateValidPlatform().ToArbitrary(),
            platform =>
            {
                var notifier = CreateChangeNotifier();
                
                var notificationCount = 0;
                
                // 订阅特定平台
                using var subscription = notifier.Subscribe(platform, _ => notificationCount++);
                
                // 通知其他平台
                notifier.NotifyChange("other-platform-1", ConfigChangeType.Created);
                notifier.NotifyChange("other-platform-2", ConfigChangeType.Updated);
                
                // 不应收到通知
                return notificationCount == 0;
            });
    }
}
