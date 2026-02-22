using System.Text;
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
using OpenDeepWiki.Services.Auth;
using OpenDeepWiki.Services.GitHub;
using OpenDeepWiki.Services.Chat;
using OpenDeepWiki.Services.MindMap;
using OpenDeepWiki.Services.Notifications;
using OpenDeepWiki.Services.OAuth;
using OpenDeepWiki.Services.Organizations;
using OpenDeepWiki.Services.Prompts;
using OpenDeepWiki.Services.Recommendation;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Translation;
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

    var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

    // MCP Google auth scheme (only when GOOGLE_CLIENT_ID is configured)
    var hasMcpAuth = !string.IsNullOrEmpty(builder.Configuration["GOOGLE_CLIENT_ID"]);
    if (hasMcpAuth)
    {
        authBuilder.AddMcpGoogleAuth(builder.Configuration);
    }

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
        if (hasMcpAuth)
        {
            options.AddPolicy(McpAuthConfiguration.McpPolicyName, policy =>
                policy.AddAuthenticationSchemes(
                        JwtBearerDefaults.AuthenticationScheme,
                        McpAuthConfiguration.McpGoogleScheme)
                    .RequireAuthenticatedUser());
        }
    });

    // 注册认证服务
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IJwtService, JwtService>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddScoped<IOAuthService, OAuthService>();
    builder.Services.AddScoped<IUserContext, UserContext>();

    // 添加HttpClient
    builder.Services.AddHttpClient();

    // 注册Git平台服务
    builder.Services.AddScoped<IGitPlatformService, GitPlatformService>();

    builder.Services.AddOptions<AiRequestOptions>()
        .Bind(builder.Configuration.GetSection("AI"))
        .PostConfigure(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = builder.Configuration["CHAT_API_KEY"];
            }

            if (string.IsNullOrWhiteSpace(options.Endpoint))
            {
                options.Endpoint = builder.Configuration["ENDPOINT"];
            }

            if (!options.RequestType.HasValue)
            {
                var requestType = builder.Configuration["CHAT_REQUEST_TYPE"];
                if (Enum.TryParse<AiRequestType>(requestType, true, out var parsed))
                {
                    options.RequestType = parsed;
                }
            }
        });

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

    // 配置 Wiki Generator
    builder.Services.AddOptions<WikiGeneratorOptions>()
        .Bind(builder.Configuration.GetSection(WikiGeneratorOptions.SectionName))
        .PostConfigure(options =>
        {
            // Catalog 配置
            var catalogModel = builder.Configuration["WIKI_CATALOG_MODEL"];
            if (!string.IsNullOrWhiteSpace(catalogModel))
            {
                options.CatalogModel = catalogModel;
            }

            var catalogEndpoint = builder.Configuration["WIKI_CATALOG_ENDPOINT"];
            if (!string.IsNullOrWhiteSpace(catalogEndpoint))
            {
                options.CatalogEndpoint = catalogEndpoint;
            }

            var catalogApiKey = builder.Configuration["WIKI_CATALOG_API_KEY"];
            if (!string.IsNullOrWhiteSpace(catalogApiKey))
            {
                options.CatalogApiKey = catalogApiKey;
            }

            var catalogRequestType = builder.Configuration["WIKI_CATALOG_REQUEST_TYPE"];
            if (Enum.TryParse<AiRequestType>(catalogRequestType, true, out var catalogParsed))
            {
                options.CatalogRequestType = catalogParsed;
            }

            // Content 配置
            var contentModel = builder.Configuration["WIKI_CONTENT_MODEL"];
            if (!string.IsNullOrWhiteSpace(contentModel))
            {
                options.ContentModel = contentModel;
            }

            var contentEndpoint = builder.Configuration["WIKI_CONTENT_ENDPOINT"];
            if (!string.IsNullOrWhiteSpace(contentEndpoint))
            {
                options.ContentEndpoint = contentEndpoint;
            }

            var contentApiKey = builder.Configuration["WIKI_CONTENT_API_KEY"];
            if (!string.IsNullOrWhiteSpace(contentApiKey))
            {
                options.ContentApiKey = contentApiKey;
            }

            var contentRequestType = builder.Configuration["WIKI_CONTENT_REQUEST_TYPE"];
            if (Enum.TryParse<AiRequestType>(contentRequestType, true, out var contentParsed))
            {
                options.ContentRequestType = contentParsed;
            }

            // Translation 配置（可选，不配置则使用 Content 配置）
            var translationModel = builder.Configuration["WIKI_TRANSLATION_MODEL"];
            if (!string.IsNullOrWhiteSpace(translationModel))
            {
                options.TranslationModel = translationModel;
            }

            var translationEndpoint = builder.Configuration["WIKI_TRANSLATION_ENDPOINT"];
            if (!string.IsNullOrWhiteSpace(translationEndpoint))
            {
                options.TranslationEndpoint = translationEndpoint;
            }

            var translationApiKey = builder.Configuration["WIKI_TRANSLATION_API_KEY"];
            if (!string.IsNullOrWhiteSpace(translationApiKey))
            {
                options.TranslationApiKey = translationApiKey;
            }

            var translationRequestType = builder.Configuration["WIKI_TRANSLATION_REQUEST_TYPE"];
            if (Enum.TryParse<AiRequestType>(translationRequestType, true, out var translationParsed))
            {
                options.TranslationRequestType = translationParsed;
            }

            // 多语言配置
            var languages = builder.Configuration["WIKI_LANGUAGES"];
            if (!string.IsNullOrWhiteSpace(languages))
            {
                options.Languages = languages;
            }
        });

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

    // 注册缓存框架（默认内存实现）
    builder.Services.AddOpenDeepWikiCache();

    // 注册处理日志服务（使用 Singleton，因为它内部使用 IServiceScopeFactory 创建独立 scope）
    builder.Services.AddSingleton<IProcessingLogService, ProcessingLogService>();

    // 注册 GitHub App 服务
    builder.Services.AddSingleton<GitHubAppCredentialCache>();
    builder.Services.AddScoped<IGitHubAppService, GitHubAppService>();
    builder.Services.AddScoped<IAdminGitHubImportService, AdminGitHubImportService>();

    // 注册管理端服务
    builder.Services.AddScoped<IAdminStatisticsService, AdminStatisticsService>();
    builder.Services.AddScoped<IAdminRepositoryService, AdminRepositoryService>();
    builder.Services.AddScoped<IAdminUserService, AdminUserService>();
    builder.Services.AddScoped<IAdminRoleService, AdminRoleService>();
    builder.Services.AddScoped<IAdminDepartmentService, AdminDepartmentService>();
    builder.Services.AddScoped<IAdminToolsService, AdminToolsService>();
    builder.Services.AddScoped<IAdminSettingsService, AdminSettingsService>();
    builder.Services.AddScoped<IAdminChatAssistantService, AdminChatAssistantService>();
    builder.Services.AddScoped<IOrganizationService, OrganizationService>();

    // 注册动态配置管理器
    builder.Services.AddScoped<IDynamicConfigManager, DynamicConfigManager>();

    // 注册推荐服务
    builder.Services.AddScoped<RecommendationService>();

    // 注册用户资料服务
    builder.Services.AddScoped<IUserProfileService, UserProfileService>();

    // 注册翻译服务
    builder.Services.AddScoped<ITranslationService, TranslationService>();

    builder.Services.AddHostedService<RepositoryProcessingWorker>();
    builder.Services.AddHostedService<TranslationWorker>();
    builder.Services.AddHostedService<MindMapWorker>();

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

    // MCP server registration (requires GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET)
    var mcpEnabled = !string.IsNullOrEmpty(builder.Configuration["GOOGLE_CLIENT_ID"])
                     && !string.IsNullOrEmpty(builder.Configuration["GOOGLE_CLIENT_SECRET"]);
    if (mcpEnabled)
    {
        builder.Services.AddScoped<IMcpUserResolver, McpUserResolver>();
        builder.Services.AddSingleton<McpOAuthServer>();
        builder.Services.AddHostedService<McpOAuthCleanupService>();
        builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<McpRepositoryTools>();
    }

    var app = builder.Build();

    // 初始化数据库
    await DbInitializer.InitializeAsync(app.Services);

    // 应用数据库中的系统设置到配置（覆盖环境变量和appsettings.json的值）
    using (var scope = app.Services.CreateScope())
    {
        var settingsService = scope.ServiceProvider.GetRequiredService<IAdminSettingsService>();
        var wikiOptions = scope.ServiceProvider.GetRequiredService<IOptions<WikiGeneratorOptions>>();
        await SystemSettingDefaults.ApplyToWikiGeneratorOptions(wikiOptions.Value, settingsService);

        // Load GitHub App credentials from DB into the in-memory cache
        var githubCache = app.Services.GetRequiredService<GitHubAppCredentialCache>();
        await githubCache.LoadFromDbAsync(settingsService);
    }

    // 启用 CORS
    app.UseCors("AllowAll");

    // Add Serilog request logging
    app.UseSerilogLogging();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference("/v1/scalar");
    }

    app.UseAuthentication();
    app.UseAuthorization();

    // MCP server endpoints (only when fully configured with OAuth)
    if (mcpEnabled)
    {
        app.MapMcpOAuthEndpoints();
        app.UseSseKeepAlive("/api/mcp");
        app.MapProtectedResourceMetadata();
        app.MapMcp("/api/mcp").RequireAuthorization(McpAuthConfiguration.McpPolicyName);
    }

    app.MapMiniApis();
    app.MapAuthEndpoints();
    app.MapOAuthEndpoints();
    app.MapAdminEndpoints();
    app.MapOrganizationEndpoints();
    app.MapChatAssistantEndpoints();
    app.MapChatAppEndpoints();
    app.MapEmbedEndpoints();

    app.MapSystemEndpoints();
    app.MapIncrementalUpdateEndpoints();

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
/// 加载 .env 文件到 Configuration
/// 支持从当前目录或应用程序目录加载 .env 文件
/// </summary>
static void LoadEnvFile(IConfigurationBuilder configuration)
{
    // 尝试多个路径查找 .env 文件
    var envPaths = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), ".env"),
        Path.Combine(AppContext.BaseDirectory, ".env")
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
            
            envVars[key] = value;
            
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