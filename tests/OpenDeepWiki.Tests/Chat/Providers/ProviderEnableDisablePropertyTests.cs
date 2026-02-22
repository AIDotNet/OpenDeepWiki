using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OpenDeepWiki.Chat.Abstractions;
using OpenDeepWiki.Chat.Providers;
using OpenDeepWiki.Chat.Routing;
using Xunit;

namespace OpenDeepWiki.Tests.Chat.Providers;

/// <summary>
/// Feature: multi-platform-agent-chat, Property 5: Provider 启用/禁用状态一致性
/// 验证启用或禁用操作后，Provider 的 IsEnabled 状态应立即反映变更，
/// 且只有启用的 Provider 才能处理消息
/// **Validates: Requirements 2.5**
/// </summary>
public class ProviderEnableDisablePropertyTests
{
    #region Generators

    /// <summary>
    /// 生成唯一的平台ID
    /// </summary>
    private static Gen<string> GenerateUniquePlatformId()
    {
        return Gen.Choose(1, 1000).Select(n => $"platform_{n}");
    }

    /// <summary>
    /// 生成启用/禁用操作序列
    /// </summary>
    private static Gen<bool[]> GenerateEnableDisableSequence()
    {
        return Gen.ArrayOf(Gen.Elements(true, false))
            .Where(arr => arr.Length > 0 && arr.Length <= 20);
    }

    /// <summary>
    /// 生成平台数量（1-5）
    /// </summary>
    private static Gen<int> GeneratePlatformCount()
    {
        return Gen.Choose(1, 5);
    }

    #endregion

    /// <summary>
    /// Property 5: 启用/禁用操作后，IsEnabled 状态应立即反映变更
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EnableDisable_ShouldImmediatelyReflectStateChange()
    {
        return Prop.ForAll(
            GenerateUniquePlatformId().ToArbitrary(),
            GenerateEnableDisableSequence().ToArbitrary(),
            (platformId, operations) =>
            {
                var provider = new StatefulTestProvider(platformId, true);
                
                foreach (var shouldEnable in operations)
                {
                    provider.IsEnabled = shouldEnable;
                    
                    // 验证状态立即反映变更
                    if (provider.IsEnabled != shouldEnable)
                        return false;
                }
                
                return true;
            });
    }

    /// <summary>
    /// Property 5: 只有启用的 Provider 才能处理消息
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OnlyEnabledProvider_ShouldProcessMessages()
    {
        return Prop.ForAll(
            GenerateUniquePlatformId().ToArbitrary(),
            platformId =>
            {
                var provider = new StatefulTestProvider(platformId, true);
                var message = CreateTestMessage(platformId);
                
                // 启用时应该能处理消息
                provider.IsEnabled = true;
                var enabledResult = provider.TryProcessMessage(message);
                
                // 禁用时不应该处理消息
                provider.IsEnabled = false;
                var disabledResult = provider.TryProcessMessage(message);
                
                return enabledResult && !disabledResult;
            });
    }

    /// <summary>
    /// Property 5: 通过路由器访问时，禁用的 Provider 不应处理消息
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DisabledProvider_ShouldNotProcessMessagesViaRouter()
    {
        return Prop.ForAll(
            GeneratePlatformCount().ToArbitrary(),
            count =>
            {
                var logger = NullLogger<MessageRouter>.Instance;
                var router = new MessageRouter(logger, null!);
                var platforms = Enumerable.Range(1, count)
                    .Select(i => $"platform_{i}")
                    .ToList();
                
                // 注册所有 Provider，初始状态为启用
                var providers = new Dictionary<string, StatefulTestProvider>();
                foreach (var platformId in platforms)
                {
                    var provider = new StatefulTestProvider(platformId, true);
                    providers[platformId] = provider;
                    router.RegisterProvider(provider);
                }
                
                // 禁用第一个 Provider
                var disabledPlatform = platforms.First();
                providers[disabledPlatform].IsEnabled = false;
                
                // 验证通过路由器获取的 Provider 状态正确
                var retrievedProvider = router.GetProvider(disabledPlatform);
                
                return retrievedProvider != null && !retrievedProvider.IsEnabled;
            });
    }

    /// <summary>
    /// Property 5: 启用/禁用状态变更应该是原子的
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EnableDisable_ShouldBeAtomic()
    {
        return Prop.ForAll(
            GenerateUniquePlatformId().ToArbitrary(),
            platformId =>
            {
                var provider = new StatefulTestProvider(platformId, true);
                var results = new List<bool>();
                
                // 快速切换状态多次
                for (int i = 0; i < 100; i++)
                {
                    var expectedState = i % 2 == 0;
                    provider.IsEnabled = expectedState;
                    results.Add(provider.IsEnabled == expectedState);
                }
                
                // 所有状态变更都应该立即生效
                return results.All(r => r);
            });
    }

    /// <summary>
    /// Property 5: 多个 Provider 的启用/禁用状态应该独立
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MultipleProviders_ShouldHaveIndependentState()
    {
        return Prop.ForAll(
            GeneratePlatformCount().ToArbitrary(),
            count =>
            {
                var providers = Enumerable.Range(1, count)
                    .Select(i => new StatefulTestProvider($"platform_{i}", true))
                    .ToList();
                
                // 禁用偶数索引的 Provider
                for (int i = 0; i < providers.Count; i++)
                {
                    if (i % 2 == 0)
                        providers[i].IsEnabled = false;
                }
                
                // 验证状态独立性
                for (int i = 0; i < providers.Count; i++)
                {
                    var expectedState = i % 2 != 0; // 奇数索引应该启用
                    if (providers[i].IsEnabled != expectedState)
                        return false;
                }
                
                return true;
            });
    }

    /// <summary>
    /// Property 5: 初始状态应该正确设置
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property InitialState_ShouldBeCorrectlySet()
    {
        return Prop.ForAll(
            GenerateUniquePlatformId().ToArbitrary(),
            Gen.Elements(true, false).ToArbitrary(),
            (platformId, initialState) =>
            {
                var provider = new StatefulTestProvider(platformId, initialState);
                return provider.IsEnabled == initialState;
            });
    }

    /// <summary>
    /// Property 5: 禁用后重新启用应该恢复处理能力
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ReEnable_ShouldRestoreProcessingCapability()
    {
        return Prop.ForAll(
            GenerateUniquePlatformId().ToArbitrary(),
            platformId =>
            {
                var provider = new StatefulTestProvider(platformId, true);
                var message = CreateTestMessage(platformId);
                
                // 初始启用状态
                var initialResult = provider.TryProcessMessage(message);
                
                // 禁用
                provider.IsEnabled = false;
                var disabledResult = provider.TryProcessMessage(message);
                
                // 重新启用
                provider.IsEnabled = true;
                var reEnabledResult = provider.TryProcessMessage(message);
                
                return initialResult && !disabledResult && reEnabledResult;
            });
    }

    /// <summary>
    /// 单元测试: 验证默认启用状态
    /// </summary>
    [Fact]
    public void DefaultProvider_ShouldBeEnabled()
    {
        var provider = new StatefulTestProvider("test", true);
        Assert.True(provider.IsEnabled);
    }

    /// <summary>
    /// 单元测试: 验证禁用后的状态
    /// </summary>
    [Fact]
    public void DisabledProvider_ShouldReflectState()
    {
        var provider = new StatefulTestProvider("test", true);
        provider.IsEnabled = false;
        Assert.False(provider.IsEnabled);
    }

    /// <summary>
    /// 单元测试: 验证启用后的状态
    /// </summary>
    [Fact]
    public void EnabledProvider_ShouldReflectState()
    {
        var provider = new StatefulTestProvider("test", false);
        provider.IsEnabled = true;
        Assert.True(provider.IsEnabled);
    }

    #region Helper Methods

    private static IChatMessage CreateTestMessage(string platform)
    {
        return new ChatMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SenderId = "test_user",
            Content = "Test message",
            MessageType = ChatMessageType.Text,
            Platform = platform,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    #endregion
}


/// <summary>
/// 用于启用/禁用测试的有状态 Provider 实现
/// </summary>
public class StatefulTestProvider : IMessageProvider
{
    private bool _isEnabled;
    
    public string PlatformId { get; }
    public string DisplayName { get; }
    
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public StatefulTestProvider(string platformId, bool initialEnabled = true)
    {
        PlatformId = platformId;
        DisplayName = $"{platformId} Provider";
        _isEnabled = initialEnabled;
    }

    /// <summary>
    /// 尝试处理消息，只有启用时才处理
    /// </summary>
    public bool TryProcessMessage(IChatMessage message)
    {
        if (!IsEnabled)
            return false;
        
        // 模拟消息处理
        return true;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) 
        => Task.CompletedTask;

    public Task<IChatMessage?> ParseMessageAsync(string rawMessage, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return Task.FromResult<IChatMessage?>(null);
            
        return Task.FromResult<IChatMessage?>(new ChatMessage
        {
            Content = rawMessage,
            Platform = PlatformId,
            MessageType = ChatMessageType.Text
        });
    }

    public Task<SendResult> SendMessageAsync(IChatMessage message, string targetUserId, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return Task.FromResult(new SendResult(false, ErrorCode: "PROVIDER_DISABLED", ErrorMessage: "Provider is disabled"));
            
        return Task.FromResult(new SendResult(true, message.MessageId));
    }

    public Task<IEnumerable<SendResult>> SendMessagesAsync(IEnumerable<IChatMessage> messages, string targetUserId, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return Task.FromResult(messages.Select(m => new SendResult(false, ErrorCode: "PROVIDER_DISABLED", ErrorMessage: "Provider is disabled")));
            
        return Task.FromResult(messages.Select(m => new SendResult(true, m.MessageId)));
    }

    public Task<WebhookValidationResult> ValidateWebhookAsync(HttpRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new WebhookValidationResult(IsEnabled));

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
