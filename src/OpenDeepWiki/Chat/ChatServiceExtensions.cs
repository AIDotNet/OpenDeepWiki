using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenDeepWiki.Chat.Callbacks;
using OpenDeepWiki.Chat.Config;
using OpenDeepWiki.Chat.Execution;
using OpenDeepWiki.Chat.Processing;
using OpenDeepWiki.Chat.Providers;
using OpenDeepWiki.Chat.Providers.Feishu;
using OpenDeepWiki.Chat.Providers.QQ;
using OpenDeepWiki.Chat.Providers.Slack;
using OpenDeepWiki.Chat.Providers.WeChat;
using OpenDeepWiki.Chat.Queue;
using OpenDeepWiki.Chat.Routing;
using OpenDeepWiki.Chat.Sessions;

namespace OpenDeepWiki.Chat;

/// <summary>
/// Chat 服务注册扩展方法
/// Requirements: 2.2, 2.4
/// </summary>
public static class ChatServiceExtensions
{
    /// <summary>
    /// 添加 Chat 系统所有服务
    /// </summary>
    public static IServiceCollection AddChatServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 注册配置选项
        services.AddChatOptions(configuration);
        
        // 注册核心服务
        services.AddChatCoreServices();
        
        // 注册 Provider
        services.AddChatProviders(configuration);
        
        // 注册后台服务
        services.AddChatBackgroundServices(configuration);
        
        // 注册配置验证启动过滤器
        services.AddChatStartupValidation(configuration);
        
        return services;
    }
    
    /// <summary>
    /// 注册配置选项
    /// </summary>
    private static IServiceCollection AddChatOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // Chat 全局配置
        services.Configure<ChatConfigOptions>(configuration.GetSection(ChatConfigOptions.SectionName));
        
        // 配置加密选项
        services.Configure<ConfigEncryptionOptions>(configuration.GetSection("Chat:Encryption"));
        
        // 会话管理配置
        services.Configure<SessionManagerOptions>(configuration.GetSection(SessionManagerOptions.SectionName));
        
        // 消息队列配置
        services.Configure<MessageQueueOptions>(configuration.GetSection(MessageQueueOptions.SectionName));
        
        // Agent 执行器配置
        services.Configure<AgentExecutorOptions>(configuration.GetSection("Chat:AgentExecutor"));
        
        // 消息处理配置
        services.Configure<ChatProcessingOptions>(configuration.GetSection(ChatProcessingOptions.SectionName));
        
        // Provider 配置
        services.Configure<FeishuProviderOptions>(configuration.GetSection("Chat:Providers:Feishu"));
        services.Configure<QQProviderOptions>(configuration.GetSection("Chat:Providers:QQ"));
        services.Configure<WeChatProviderOptions>(configuration.GetSection("Chat:Providers:WeChat"));
        services.Configure<SlackProviderOptions>(configuration.GetSection("Chat:Providers:Slack"));
        
        return services;
    }
    
    /// <summary>
    /// 注册核心服务
    /// </summary>
    private static IServiceCollection AddChatCoreServices(this IServiceCollection services)
    {
        // 配置加密服务
        services.TryAddSingleton<IConfigEncryption, AesConfigEncryption>();
        
        // 配置管理服务
        services.TryAddScoped<IChatConfigService, ChatConfigService>();
        
        // 会话管理服务
        services.TryAddScoped<ISessionManager, SessionManager>();
        
        // 消息队列服务
        services.TryAddScoped<IMessageQueue, DatabaseMessageQueue>();
        
        // 消息合并器
        services.TryAddSingleton<IMessageMerger, TextMessageMerger>();
        
        // User identity resolver (Singleton with in-memory cache)
        services.AddHttpClient<ChatUserResolver>();
        services.TryAddSingleton<IChatUserResolver, ChatUserResolver>();

        // Agent 执行器
        services.TryAddScoped<IAgentExecutor, AgentExecutor>();
        
        // 消息路由器（Singleton，因为需要维护 Provider 注册表）
        // Uses IServiceScopeFactory to resolve scoped deps (SessionManager, MessageQueue, etc.)
        services.TryAddSingleton<IMessageRouter>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<MessageRouter>>();
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            return new MessageRouter(logger, scopeFactory);
        });
        
        // 消息回调管理器
        services.TryAddScoped<IMessageCallback>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<CallbackManager>>();
            var messageQueue = sp.GetRequiredService<IMessageQueue>();
            var router = sp.GetRequiredService<IMessageRouter>();
            
            return new CallbackManager(
                logger,
                messageQueue,
                platform => router.GetProvider(platform));
        });
        
        // 配置变更通知器（Singleton）
        services.TryAddSingleton<IConfigChangeNotifier, ConfigChangeNotifier>();
        
        return services;
    }
    
    /// <summary>
    /// 注册 Provider
    /// Requirements: 2.2 - 通过依赖注入自动发现并加载
    /// </summary>
    private static IServiceCollection AddChatProviders(this IServiceCollection services, IConfiguration configuration)
    {
        // 注册 HttpClient 工厂
        services.AddHttpClient<FeishuProvider>();
        services.AddHttpClient<QQProvider>();
        services.AddHttpClient<WeChatProvider>();
        services.AddHttpClient<SlackProvider>();
        
        // 注册 Provider 为 Scoped 服务
        services.TryAddScoped<FeishuProvider>();
        services.TryAddScoped<QQProvider>();
        services.TryAddScoped<WeChatProvider>();
        services.TryAddScoped<SlackProvider>();
        
        // 注册 Provider 集合（用于自动发现）
        services.TryAddScoped<IEnumerable<IMessageProvider>>(sp => new IMessageProvider[]
        {
            sp.GetRequiredService<FeishuProvider>(),
            sp.GetRequiredService<QQProvider>(),
            sp.GetRequiredService<WeChatProvider>(),
            sp.GetRequiredService<SlackProvider>()
        });
        
        // 注册 Provider 初始化服务
        services.AddHostedService<ProviderInitializationService>();
        
        return services;
    }
    
    /// <summary>
    /// 注册后台服务
    /// </summary>
    private static IServiceCollection AddChatBackgroundServices(this IServiceCollection services, IConfiguration configuration)
    {
        var processingOptions = configuration.GetSection(ChatProcessingOptions.SectionName).Get<ChatProcessingOptions>();
        
        // 只有启用时才注册消息处理 Worker
        if (processingOptions?.Enabled ?? true)
        {
            services.AddHostedService<ChatMessageProcessingWorker>();
        }
        
        // 配置热重载服务
        var chatOptions = configuration.GetSection(ChatConfigOptions.SectionName).Get<ChatConfigOptions>();
        if (chatOptions?.EnableHotReload ?? true)
        {
            services.AddHostedService<ConfigReloadService>();
        }
        
        return services;
    }
    
    /// <summary>
    /// 注册启动验证
    /// </summary>
    private static IServiceCollection AddChatStartupValidation(this IServiceCollection services, IConfiguration configuration)
    {
        var chatOptions = configuration.GetSection(ChatConfigOptions.SectionName).Get<ChatConfigOptions>();
        
        // 只有启用时才注册配置验证启动过滤器
        if (chatOptions?.ValidateOnStartup ?? true)
        {
            services.AddTransient<IStartupFilter, ConfigValidationStartupFilter>();
        }
        
        return services;
    }
}
