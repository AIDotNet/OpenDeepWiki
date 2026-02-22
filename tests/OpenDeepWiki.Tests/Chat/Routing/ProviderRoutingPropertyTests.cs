using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenDeepWiki.Chat.Abstractions;
using OpenDeepWiki.Chat.Providers;
using OpenDeepWiki.Chat.Routing;
using Xunit;

namespace OpenDeepWiki.Tests.Chat.Routing;

/// <summary>
/// Feature: multi-platform-agent-chat, Property 4: Provider 路由正确性
/// 验证消息应被路由到与其 Platform 字段匹配的 Provider
/// **Validates: Requirements 2.3, 7.2**
/// </summary>
public class ProviderRoutingPropertyTests
{
    private readonly ILogger<MessageRouter> _logger = NullLogger<MessageRouter>.Instance;

    #region Generators

    /// <summary>
    /// 生成有效的平台标识
    /// </summary>
    private static Gen<string> GenerateValidPlatform()
    {
        return Gen.Elements("feishu", "qq", "wechat", "telegram", "slack", "discord");
    }

    /// <summary>
    /// 生成平台数量（1-6）
    /// </summary>
    private static Gen<int> GeneratePlatformCount()
    {
        return Gen.Choose(1, 6);
    }

    /// <summary>
    /// 生成唯一的平台ID
    /// </summary>
    private static Gen<string> GenerateUniquePlatformId()
    {
        return Gen.Choose(1, 1000).Select(n => $"platform_{n}");
    }

    #endregion

    /// <summary>
    /// Property 4: 对于任意消息和已注册的 Provider 集合，消息应被路由到与其 Platform 字段匹配的 Provider
    /// **Validates: Requirements 2.3, 7.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MessageShouldBeRoutedToMatchingProvider()
    {
        return Prop.ForAll(
            GeneratePlatformCount().ToArbitrary(),
            count =>
            {
                var router = new MessageRouter(_logger, null!);
                var platforms = Enumerable.Range(1, count)
                    .Select(i => $"platform_{i}")
                    .ToList();
                
                // 注册所有 Provider
                foreach (var platformId in platforms)
                {
                    var provider = CreateTestProvider(platformId);
                    router.RegisterProvider(provider);
                }

                // 验证每个平台都能正确路由
                foreach (var platformId in platforms)
                {
                    var provider = router.GetProvider(platformId);
                    if (provider == null || provider.PlatformId != platformId)
                        return false;
                }

                return true;
            });
    }

    /// <summary>
    /// Property: 注册的 Provider 数量应与 GetAllProviders 返回的数量一致
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RegisteredProviderCountShouldMatch()
    {
        return Prop.ForAll(
            GeneratePlatformCount().ToArbitrary(),
            count =>
            {
                var router = new MessageRouter(_logger, null!);
                var platforms = Enumerable.Range(1, count)
                    .Select(i => $"platform_{i}")
                    .ToList();
                
                foreach (var platformId in platforms)
                {
                    var provider = CreateTestProvider(platformId);
                    router.RegisterProvider(provider);
                }

                return router.GetAllProviders().Count() == platforms.Count;
            });
    }


    /// <summary>
    /// Property: 未注册的平台应返回 null
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UnregisteredPlatformShouldReturnNull()
    {
        return Prop.ForAll(
            GeneratePlatformCount().ToArbitrary(),
            GenerateUniquePlatformId().ToArbitrary(),
            (count, unregisteredPlatform) =>
            {
                var router = new MessageRouter(_logger, null!);
                var platforms = Enumerable.Range(1, count)
                    .Select(i => $"registered_{i}")
                    .ToList();
                
                // 注册一些 Provider
                foreach (var platformId in platforms)
                {
                    var provider = CreateTestProvider(platformId);
                    router.RegisterProvider(provider);
                }

                // 确保未注册的平台不在已注册列表中
                if (platforms.Contains(unregisteredPlatform))
                    return true; // 跳过这个测试用例

                return router.GetProvider(unregisteredPlatform) == null;
            });
    }

    /// <summary>
    /// Property: HasProvider 应与 GetProvider 结果一致
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property HasProviderShouldBeConsistentWithGetProvider()
    {
        return Prop.ForAll(
            GeneratePlatformCount().ToArbitrary(),
            GenerateValidPlatform().ToArbitrary(),
            (count, queryPlatform) =>
            {
                var router = new MessageRouter(_logger, null!);
                var platforms = Enumerable.Range(1, count)
                    .Select(i => $"platform_{i}")
                    .ToList();
                
                foreach (var platformId in platforms)
                {
                    var provider = CreateTestProvider(platformId);
                    router.RegisterProvider(provider);
                }

                var hasProvider = router.HasProvider(queryPlatform);
                var getProvider = router.GetProvider(queryPlatform);

                return hasProvider == (getProvider != null);
            });
    }

    /// <summary>
    /// Property: 注销 Provider 后应无法获取
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UnregisteredProviderShouldNotBeAccessible()
    {
        return Prop.ForAll(
            GeneratePlatformCount().ToArbitrary(),
            count =>
            {
                var router = new MessageRouter(_logger, null!);
                var platforms = Enumerable.Range(1, count)
                    .Select(i => $"platform_{i}")
                    .ToList();
                
                // 注册所有 Provider
                foreach (var platformId in platforms)
                {
                    var provider = CreateTestProvider(platformId);
                    router.RegisterProvider(provider);
                }

                // 选择第一个进行注销
                var platformToUnregister = platforms.First();
                var unregisterResult = router.UnregisterProvider(platformToUnregister);

                // 验证注销成功且无法再获取
                return unregisterResult && 
                       router.GetProvider(platformToUnregister) == null &&
                       !router.HasProvider(platformToUnregister);
            });
    }

    /// <summary>
    /// Property: 重复注册同一平台应更新 Provider
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DuplicateRegistrationShouldUpdateProvider()
    {
        return Prop.ForAll(
            GenerateUniquePlatformId().ToArbitrary(),
            platformId =>
            {
                var router = new MessageRouter(_logger, null!);
                
                // 注册第一个 Provider
                var provider1 = CreateTestProvider(platformId, "Provider 1");
                router.RegisterProvider(provider1);

                // 注册第二个同平台的 Provider
                var provider2 = CreateTestProvider(platformId, "Provider 2");
                router.RegisterProvider(provider2);

                // 验证获取的是最新的 Provider
                var currentProvider = router.GetProvider(platformId);
                return currentProvider != null && 
                       currentProvider.DisplayName == "Provider 2" &&
                       router.GetAllProviders().Count() == 1;
            });
    }

    /// <summary>
    /// Property: 平台标识匹配应不区分大小写
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Fact]
    public void PlatformIdMatching_ShouldBeCaseInsensitive()
    {
        var router = new MessageRouter(_logger, null!);
        
        // 注册小写平台
        var provider = CreateTestProvider("feishu", "Feishu Provider");
        router.RegisterProvider(provider);

        // 使用不同大小写查询
        var result1 = router.GetProvider("feishu");
        var result2 = router.GetProvider("FEISHU");
        var result3 = router.GetProvider("Feishu");

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotNull(result3);
        Assert.Equal(result1.PlatformId, result2.PlatformId);
        Assert.Equal(result2.PlatformId, result3.PlatformId);
    }

    /// <summary>
    /// Property: 空平台标识应返回 null
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyPlatformId_ShouldReturnNull(string? platformId)
    {
        var router = new MessageRouter(_logger, null!);
        
        var result = router.GetProvider(platformId!);
        
        Assert.Null(result);
    }

    #region Helper Methods

    private static TestRoutingProvider CreateTestProvider(string platformId, string? displayName = null)
    {
        return new TestRoutingProvider(platformId, displayName ?? $"{platformId} Provider");
    }

    #endregion
}

/// <summary>
/// 用于路由测试的简单 Provider 实现
/// </summary>
public class TestRoutingProvider : IMessageProvider
{
    public string PlatformId { get; }
    public string DisplayName { get; }
    public bool IsEnabled { get; set; } = true;

    public TestRoutingProvider(string platformId, string displayName)
    {
        PlatformId = platformId;
        DisplayName = displayName;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) 
        => Task.CompletedTask;

    public Task<IChatMessage?> ParseMessageAsync(string rawMessage, CancellationToken cancellationToken = default)
        => Task.FromResult<IChatMessage?>(new ChatMessage
        {
            Content = rawMessage,
            Platform = PlatformId,
            MessageType = ChatMessageType.Text
        });

    public Task<SendResult> SendMessageAsync(IChatMessage message, string targetUserId, CancellationToken cancellationToken = default)
        => Task.FromResult(new SendResult(true, message.MessageId));

    public Task<IEnumerable<SendResult>> SendMessagesAsync(IEnumerable<IChatMessage> messages, string targetUserId, CancellationToken cancellationToken = default)
        => Task.FromResult(messages.Select(m => new SendResult(true, m.MessageId)));

    public Task<WebhookValidationResult> ValidateWebhookAsync(HttpRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new WebhookValidationResult(true));

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
