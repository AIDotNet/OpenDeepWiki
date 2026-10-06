using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenDeepWiki.Agents;
using OpenDeepWiki.Cache.DependencyInjection;
using OpenDeepWiki.Chat;
using OpenDeepWiki.MCP;
using OpenDeepWiki.Endpoints;
using OpenDeepWiki.Endpoints.Admin;
using OpenDeepWiki.Infrastructure;
using OpenDeepWiki.Services.Admin;
using OpenDeepWiki.Services.AI;
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.GitHub;
using OpenDeepWiki.Services.Graphify;
using OpenDeepWiki.Services.Chat;
using OpenDeepWiki.Services.MindMap;
using OpenDeepWiki.Services.Notifications;
using OpenDeepWiki.Services.OAuth;
using OpenDeepWiki.Services.Organizations;
using OpenDeepWiki.Services.Prompts;
using OpenDeepWiki.Services.Recommendation;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Translation;
using OpenDeepWiki.Services.Mcp;
using OpenDeepWiki.Services.UserProfile;
using OpenDeepWiki.Services.Wiki;
using Scalar.AspNetCore;
using Serilog;

// Bootstrap logger for startup error capture
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // ASCII Art Banner
    var banner = """
    
     ██████╗ ██████╗ ███████╗███╗   ██╗██████╗ ███████╗███████╗██████╗ ██╗    ██╗██╗██╗  ██╗██╗
    ██╔═══██╗██╔══██╗██╔════╝████╗  ██║██╔══██╗██╔════╝██╔════╝██╔══██╗██║    ██║██║██║ ██╔╝██║
    ██║   ██║██████╔╝█████╗  ██╔██╗ ██║██║  ██║█████╗  █████╗  ██████╔╝██║ █╗ ██║██║█████╔╝ ██║
    ██║   ██║██╔═══╝ ██╔══╝  ██║╚██╗██║██║  ██║██╔══╝  ██╔══╝  ██╔═══╝ ██║███╗██║██║██╔═██╗ ██║
    ╚██████╔╝██║     ███████╗██║ ╚████║██████╔╝███████╗███████╗██║     ╚███╔███╔╝██║██║  ██╗██║
     ╚═════╝ ╚═╝     ╚══════╝╚═╝  ╚═══╝╚═════╝ ╚══════╝╚══════╝╚═╝      ╚══╝╚══╝ ╚═╝╚═╝  ╚═╝╚═╝
                                                                                    
                             ██████╗  ██████╗ ██╗
                            ██╔════╝ ██╔═══██╗██║
                            ██║  ███╗██║   ██║██║
                            ██║   ██║██║   ██║╚═╝
                            ╚██████╔╝╚██████╔╝██╗
                             ╚═════╝  ╚═════╝ ╚═╝
    
    """;
    Console.WriteLine(banner);
    
    Log.Information("Starting OpenDeepWiki application");

    var builder = WebApplication.CreateBuilder(args);

    // 加载 .env 文件到 Configuration
    LoadEnvFile(builder.Configuration);

    // Add Serilog logging
    builder.AddSerilogLogging();

    // Add services to the container.
    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi();
    builder.Services.AddMiniApis();

    // 根据配置添加数据库服务
    builder.Services.AddDatabase(builder.Configuration);

    // 配置JWT
    builder.Services.AddOptions<JwtOptions>()
        .Bind(builder.Configuration.GetSection("Jwt"))
        .PostConfigure(options =>
        {
            if (string.IsNullOrWhiteSpace(options.SecretKey))
            {
                options.SecretKey = builder.Configuration["JWT_SECRET_KEY"]
                    ?? throw new InvalidOperationException("JWT密钥未配置");
            }
        });

    // 添加JWT认证
    var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
    var secretKey = jwtOptions.SecretKey;
    if (string.IsNullOrWhiteSpace(secretKey))
    {
        secretKey = builder.Configuration["JWT_SECRET_KEY"]
            ?? "OpenDeepWiki-Default-Secret-Key-Please-Change-In-Production-Environment-2024";
    }

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    });

    // 注册认证服务
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IJwtService, JwtService>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IOAuthService, OAuthService>();
    builder.Services.AddScoped<IUserContext, UserContext>();

    // 添加HttpClient
    builder.Services.AddHttpClient();

    var mcpOAuthEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["GOOGLE_CLIENT_ID"] ?? builder.Configuration["Google:ClientId"])
                          && !string.IsNullOrWhiteSpace(builder.Configuration["GOOGLE_CLIENT_SECRET"] ?? builder.Configuration["Google:ClientSecret"]);
    if (mcpOAuthEnabled)
    {
        builder.Services.AddSingleton<McpOAuthServer>();
        builder.Services.AddHostedService<McpOAuthCleanupService>();
    }

    // 注册Git平台服务
    builder.Services.AddScoped<IGitPlatformService, GitPlatformService>();

    builder.Services.AddOptions<AiRequestOptions>()
        .Bind(builder.Configuration.GetSection("AI"));

    builder.Services
        .AddCors(options =>
        {
            options.AddPolicy("AllowAll",
                policyBuilder => policyBuilder
                    .SetIsOriginAllowed(_ => true)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());
        });
    builder.Services.AddSingleton<AgentFactory>();
    builder.Services.AddSingleton<IAiProviderPresetCatalog, AiProviderPresetCatalog>();
    builder.Services.AddScoped<IAiProviderResolver, AiProviderResolver>();
    builder.Services.AddScoped<IAiProviderPresetSeeder, AiProviderPresetSeeder>();
    builder.Services.AddScoped<IAiConfigurationMigrationService, AiConfigurationMigrationService>();

    // 配置 Repository Analyzer
    builder.Services.AddOptions<RepositoryAnalyzerOptions>()
        .Bind(builder.Configuration.GetSection("RepositoryAnalyzer"))
        .PostConfigure(options =>
        {
            var repoDir = builder.Configuration["REPOSITORIES_DIRECTORY"];
            if (!string.IsNullOrWhiteSpace(repoDir))
            {
                options.RepositoriesDirectory = repoDir;
            }
        });
    builder.Services.AddScoped<IRepositoryAnalyzer, RepositoryAnalyzer>();
    builder.Services.AddScoped<IRepositoryFullRegenerationCleaner, RepositoryFullRegenerationCleaner>();
    builder.Services.AddScoped<IBranchFullGenerationCleaner, BranchFullGenerationCleaner>();
    builder.Services.AddSingleton<WikiGenerationInstanceIdentity>();
    builder.Services.AddScoped<IRepositoryGenerationLockService, RepositoryGenerationLockService>();
    builder.Services.AddScoped<IWikiGenerationCoordinator, WikiGenerationCoordinator>();
    builder.Services.AddScoped<IRepositoryBranchProcessor, RepositoryBranchProcessor>();
    builder.Services.AddScoped<IBranchGenerationTaskService, BranchGenerationTaskService>();
    builder.Services.AddScoped<IRepositoryScanPlanResolver, RepositoryScanPlanResolver>();

    // Configure Graphify artifact generation
    builder.Services.AddOptions<GraphifyOptions>()
        .Bind(builder.Configuration.GetSection("Graphify"));
    builder.Services.AddScoped<IGraphifyCliRunner, GraphifyCliRunner>();

    // Opt-in publish to the understand-quickly registry of code-knowledge graphs.
    // https://github.com/looptech-ai/understand-quickly
    builder.Services.AddOptions<UnderstandQuicklyOptions>()
        .Bind(builder.Configuration.GetSection(UnderstandQuicklyOptions.SectionName));
    builder.Services
        .AddHttpClient<IUnderstandQuicklyPublisher, UnderstandQuicklyPublisher>();

    // 配置 Wiki Generator
    builder.Services.AddOptions<WikiGeneratorOptions>()
        .Bind(builder.Configuration.GetSection(WikiGeneratorOptions.SectionName))
        .PostConfigure(options => WikiGeneratorOptionsConfigurator.Apply(options, builder.Configuration));

    // 注册 Prompt Plugin
    builder.Services.AddSingleton<IPromptPlugin>(sp =>
    {
        var options = sp.GetRequiredService<IOptions<WikiGeneratorOptions>>().Value;
        var promptsDir = Path.Combine(AppContext.BaseDirectory, options.PromptsDirectory);

        // Fallback to current directory if base directory doesn't have prompts
        if (!Directory.Exists(promptsDir))
        {
            promptsDir = Path.Combine(Directory.GetCurrentDirectory(), options.PromptsDirectory);
        }

        return new FilePromptPlugin(promptsDir);
    });

    // 注册 Wiki Generator
    builder.Services.AddScoped<IWikiGenerator, WikiGenerator>();
    builder.Services.AddScoped<IRepositorySkillMarkdownBuilder, RepositorySkillMarkdownBuilder>();

    // 注册缓存框架（默认内存实现）
    builder.Services.AddOpenDeepWikiCache();

    // 全局限流：爬虫风暴的兜底保护，防止并发抓取耗尽数据库连接
    // 正常用户远达不到该阈值；登录用户（带 Authorization）额度更高
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetTokenBucketLimiter(
                BuildClientPartitionKey(context),
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = context.User.Identity?.IsAuthenticated == true ? 600 : 300,
                    TokensPerPeriod = 150,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
        options.OnRejected = static (context, _) =>
        {
            context.HttpContext.Response.Headers.RetryAfter = "10";
            return ValueTask.CompletedTask;
        };
    });

    // 注册处理日志服务（使用 Singleton，因为它内部使用 IServiceScopeFactory 创建独立 scope）
    builder.Services.AddSingleton<IProcessingLogService, ProcessingLogService>();

    // Register GitHub App services
    builder.Services.AddSingleton<GitHubAppCredentialCache>();
    builder.Services.AddScoped<IGitHubAppService, GitHubAppService>();
    builder.Services.AddScoped<IAdminGitHubImportService, AdminGitHubImportService>();
    builder.Services.AddScoped<IUserGitHubImportService, UserGitHubImportService>();

    // 注册管理端服务
    builder.Services.AddScoped<IAdminStatisticsService, AdminStatisticsService>();
    builder.Services.AddScoped<IAdminRepositoryService, AdminRepositoryService>();
    builder.Services.AddScoped<IAdminUserService, AdminUserService>();
    builder.Services.AddScoped<IAdminRoleService, AdminRoleService>();
    builder.Services.AddScoped<IAdminDepartmentService, AdminDepartmentService>();
    builder.Services.AddScoped<IAdminToolsService, AdminToolsService>();
    builder.Services.AddScoped<IAdminSettingsService, AdminSettingsService>();
    builder.Services.AddScoped<IAdminChatAssistantService, AdminChatAssistantService>();
    builder.Services.AddScoped<IAdminApiKeyService, AdminApiKeyService>();
    builder.Services.AddScoped<IOrganizationService, OrganizationService>();
    builder.Services.AddScoped<IGraphifyArtifactService, GraphifyArtifactService>();

    // 注册动态配置管理器
    builder.Services.AddScoped<IDynamicConfigManager, DynamicConfigManager>();

    // 注册推荐服务
    builder.Services.AddScoped<RecommendationService>();

    // 注册用户资料服务
    builder.Services.AddScoped<IUserProfileService, UserProfileService>();

    // 注册翻译服务
    builder.Services.AddScoped<ITranslationService, TranslationService>();

    builder.Services.AddHostedService<RepositoryProcessingWorker>();
    builder.Services.AddHostedService<BranchGenerationWorker>();
    builder.Services.AddHostedService<TranslationWorker>();
    builder.Services.AddHostedService<MindMapWorker>();
    builder.Services.AddHostedService<GraphifyArtifactWorker>();

    // 配置增量更新选项
    // Requirements: 6.2, 6.3, 6.6 - 可配置的更新间隔
    builder.Services.AddOptions<IncrementalUpdateOptions>()
        .Bind(builder.Configuration.GetSection(IncrementalUpdateOptions.SectionName));

    // 注册增量更新服务
    // Requirements: 2.1 - 增量更新服务接口
    builder.Services.AddScoped<IIncrementalUpdateService, IncrementalUpdateService>();

    // 注册订阅者通知服务（空实现）
    // Requirements: 4.1 - 订阅者通知服务接口
    builder.Services.AddScoped<ISubscriberNotificationService, NullSubscriberNotificationService>();

    // 注册增量更新后台工作器
    // Requirements: 1.1 - 独立的增量更新后台工作器
    builder.Services.AddHostedService<IncrementalUpdateWorker>();

    // 注册 Chat 系统服务
    // Requirements: 2.2, 2.4 - 通过依赖注入自动发现并加载 Provider
    builder.Services.AddChatServices(builder.Configuration);

    // 注册对话助手服务
    // Requirements: 2.4, 3.1, 9.1 - 对话助手API服务
    builder.Services.AddScoped<IMcpToolConverter, McpToolConverter>();
    builder.Services.AddScoped<ISkillToolConverter, SkillToolConverter>();
    builder.Services.AddScoped<IChatAssistantService, ChatAssistantService>();
    builder.Services.AddScoped<IChatShareService, ChatShareService>();

    // 注册用户应用管理服务
    // Requirements: 12.2, 12.6, 12.7 - 用户应用CRUD和密钥管理
    builder.Services.AddScoped<IChatAppService, ChatAppService>();

    // 注册应用统计服务
    // Requirements: 15.1, 15.2, 15.3, 15.4, 15.5, 15.7 - 统计数据记录和查询
    builder.Services.AddScoped<IAppStatisticsService, AppStatisticsService>();

    // 注册提问记录服务
    // Requirements: 16.1, 16.2, 16.3, 16.4, 16.5 - 提问记录和查询
    builder.Services.AddScoped<IChatLogService, ChatLogService>();

    // 注册嵌入服务
    // Requirements: 13.5, 13.6, 14.2, 14.7, 17.1, 17.2, 17.4 - 嵌入脚本验证和对话
    builder.Services.AddScoped<IEmbedService, EmbedService>();

    // 注册 MCP 提供商管理服务
    builder.Services.AddScoped<IAdminMcpProviderService, AdminMcpProviderService>();
    builder.Services.AddScoped<IMcpUsageLogService, McpUsageLogService>();
    builder.Services.AddHostedService<McpStatisticsAggregationService>();

    // MCP server registration (official MCP server + scope via ConfigureSessionOptions)
    var mcpEnabled = builder.Configuration.GetValue("MCP_ENABLED", true);
    if (mcpEnabled)
    {
        builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.ConfigureSessionOptions = (httpContext, mcpServer, _) =>
                {
                    var segments = httpContext.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries)
                                   ?? Array.Empty<string>();
                    string? owner = null;
                    string? repo = null;
                    if (segments.Length >= 4
                        && string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(segments[1], "mcp", StringComparison.OrdinalIgnoreCase))
                    {
                        owner = Uri.UnescapeDataString(segments[2]);
                        repo = Uri.UnescapeDataString(segments[3]);
                    }

                    if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
                    {
                        var query = httpContext.Request.Query;
                        var queryOwner = query["owner"].FirstOrDefault();
                        var queryRepo = query["repo"].FirstOrDefault() ?? query["name"].FirstOrDefault();

                        if (!string.IsNullOrWhiteSpace(queryOwner))
                        {
                            owner = Uri.UnescapeDataString(queryOwner);
                        }

                        if (!string.IsNullOrWhiteSpace(queryRepo))
                        {
                            repo = Uri.UnescapeDataString(queryRepo);
                        }
                    }

                    McpRepositoryScopeAccessor.SetScope(mcpServer, owner, repo);
                    return Task.CompletedTask;
                };
            })
            .WithTools<McpGlobalTools>()
            .WithTools<McpRepositoryTools>();
    }

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    app.UseCors("AllowAll");

    // 添加静态文件支持（用于 Skill assets 等）
    app.UseStaticFiles();

    // Configure forwarded headers for reverse proxy scenarios
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                           | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                           | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference("/v1/scalar");
    }

    app.UseAuthentication();
    app.UseAuthorization();

    // 限流放在认证之后，使分区策略能区分登录用户与匿名流量
    app.UseRateLimiter();

    // MCP server endpoints (official MCP server + scope via ConfigureSessionOptions)
    if (mcpEnabled)
    {
        app.UseMcpUsageLogging("/api/mcp");
        app.UseSseKeepAlive("/api/mcp");
        app.MapMcp("/api/mcp");
        app.MapMcp("/api/mcp/{owner}/{repo}");

        if (mcpOAuthEnabled)
        {
            app.MapProtectedResourceMetadata();
            app.MapMcpOAuthEndpoints();
        }
    }

    app.MapMiniApis();
    app.MapRepositoryUploadEndpoints();
    app.MapAuthEndpoints();
    app.MapApiKeyEndpoints();
    app.MapOAuthEndpoints();
    app.MapAdminEndpoints();
    app.MapGitHubImportEndpoints();
    app.MapOrganizationEndpoints();
    app.MapChatAssistantEndpoints();
    app.MapChatAppEndpoints();
    app.MapEmbedEndpoints();

    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
    app.MapSystemEndpoints();
    app.MapIncrementalUpdateEndpoints();
    app.MapBranchGenerationEndpoints();
    app.MapMcpProviderEndpoints();

    // 初始化数据库（创建默认数据）
    using (var scope = app.Services.CreateScope())
    {
        await DbInitializer.InitializeAsync(scope.ServiceProvider);

        // 从数据库系统设置加载 AI 配置（覆盖环境变量/appsettings 的默认值）
        var settingsService = scope.ServiceProvider.GetRequiredService<IAdminSettingsService>();
        var wikiOptions = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<WikiGeneratorOptions>>().CurrentValue;
        await SystemSettingDefaults.ApplyToWikiGeneratorOptions(wikiOptions, settingsService);
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// 限流分区 key：优先取反代透传的真实客户端 IP，认证用户叠加用户标识
/// </summary>
static string BuildClientPartitionKey(HttpContext context)
{
    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var user = context.User.Identity?.Name;
    return user is null ? ip : $"{ip}|{user}";
}

/// <summary>
/// 加载 .env 文件到 Configuration
/// 支持从当前目录或应用程序目录加载 .env 文件
/// </summary>
static void LoadEnvFile(IConfigurationBuilder configuration)
{
    // 尝试多个路径查找 .env 文件
    var envPaths = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), ".env"),
        Path.Combine(AppContext.BaseDirectory, ".env"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".env"),
    };

    foreach (var envPath in envPaths)
    {
        if (!File.Exists(envPath)) continue;
        
        Log.Information("Loading .env file from: {EnvPath}", envPath);
        var envVars = new Dictionary<string, string?>();
        
        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmedLine = line.Trim();
            
            // 跳过空行和注释
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith('#'))
                continue;

            var separatorIndex = trimmedLine.IndexOf('=');
            if (separatorIndex <= 0) continue;
            
            var key = trimmedLine[..separatorIndex].Trim();
            var value = trimmedLine[(separatorIndex + 1)..].Trim();
            
            // 移除引号
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value[1..^1];
            }
            
            envVars[NormalizeEnvKey(key)] = value;
            
            // 同时设置到环境变量，以便其他地方使用
            Environment.SetEnvironmentVariable(key, value);
        }
        
        // 添加到 Configuration
        configuration.AddInMemoryCollection(envVars);
        Log.Information("Loaded {Count} environment variables from .env file", envVars.Count);
        return;
    }
    
    Log.Information("No .env file found, using system environment variables only");
}

/// <summary>
/// 将 .env 文件中的变量名转换为 .NET 配置键：
/// - "__" 转换为 ":"（与标准环境变量命名一致，使嵌套/数组配置可被绑定）
/// - LOCAL_IMPORT_ROOT 映射为 RepositoryAnalyzer:AllowedLocalPathRoots:0
/// </summary>
static string NormalizeEnvKey(string key)
{
    if (key.Equals("LOCAL_IMPORT_ROOT", StringComparison.OrdinalIgnoreCase))
    {
        return "RepositoryAnalyzer:AllowedLocalPathRoots:0";
    }

    return key.Replace("__", ":");
}
