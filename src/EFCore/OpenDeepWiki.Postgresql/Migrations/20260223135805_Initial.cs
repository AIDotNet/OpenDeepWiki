using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenDeepWiki.Postgresql.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppStatistics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestCount = table.Column<long>(type: "bigint", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppStatistics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatApps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IconUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AppSecret = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EnableDomainValidation = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedDomains = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ProviderType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AvailableModels = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DefaultModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RateLimitPerMinute = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatApps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatAssistantConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    EnabledModelIds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EnabledMcpIds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EnabledSkillIds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DefaultModelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EnableImageUpload = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatAssistantConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Question = table.Column<string>(type: "text", nullable: false),
                    AnswerSummary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    ModelUsed = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceDomain = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessageQueues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MessageContent = table.Column<string>(type: "text", nullable: false),
                    QueueType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessageQueues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatProviderConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigData = table.Column<string>(type: "text", nullable: false),
                    WebhookUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MessageInterval = table.Column<int>(type: "integer", nullable: false),
                    MaxRetryCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatProviderConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatShareSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShareId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatShareSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Departments_Departments_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Departments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "McpConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ServerUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpDailyStatistics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    McpProviderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequestCount = table.Column<long>(type: "bigint", nullable: false),
                    SuccessCount = table.Column<long>(type: "bigint", nullable: false),
                    ErrorCount = table.Column<long>(type: "bigint", nullable: false),
                    TotalDurationMs = table.Column<long>(type: "bigint", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpDailyStatistics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpProviders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ServerUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TransportType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequiresApiKey = table.Column<bool>(type: "boolean", nullable: false),
                    ApiKeyObtainUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SystemApiKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ModelConfigId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestTypes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AllowedTools = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IconUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MaxRequestsPerDay = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpUsageLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    McpProviderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ToolName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ResponseStatus = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpUsageLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ApiKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OAuthProviders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthorizationUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TokenUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UserInfoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientSecret = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RedirectUri = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Scope = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UserInfoMapping = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RequireEmailVerification = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystemRole = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SkillConfigs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    License = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Compatibility = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AllowedTools = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FolderPath = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Author = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HasScripts = table.Column<bool>(type: "boolean", nullable: false),
                    HasReferences = table.Column<bool>(type: "boolean", nullable: false),
                    HasAssets = table.Column<bool>(type: "boolean", nullable: false),
                    SkillMdSize = table.Column<long>(type: "bigint", nullable: false),
                    TotalSize = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPreferenceCaches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    LanguageWeights = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TopicWeights = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PrivateRepoLanguages = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LastCalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferenceCaches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Password = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Avatar = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLoginIp = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessageHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SenderId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    MessageType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MessageTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessageHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessageHistories_ChatSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocalStorages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FileExtension = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UploaderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    BusinessId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    BusinessType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    AccessCount = table.Column<int>(type: "integer", nullable: false),
                    LastAccessAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Metadata = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalStorages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocalStorages_Users_UploaderId",
                        column: x => x.UploaderId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    GitUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RepoName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OrgName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AuthAccount = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AuthPassword = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StarCount = table.Column<int>(type: "integer", nullable: false),
                    ForkCount = table.Column<int>(type: "integer", nullable: false),
                    BookmarkCount = table.Column<int>(type: "integer", nullable: false),
                    SubscriptionCount = table.Column<int>(type: "integer", nullable: false),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    PrimaryLanguage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UpdateIntervalMinutes = table.Column<int>(type: "integer", nullable: true),
                    LastUpdateCheckAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Repositories_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserDepartments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    DepartmentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    IsManager = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDepartments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDepartments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserDepartments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserOAuths",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    OAuthProviderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    OAuthUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OAuthUserName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OAuthUserEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OAuthUserAvatar = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AccessToken = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RefreshToken = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Scope = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TokenType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsBound = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserOAuths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserOAuths_OAuthProviders_OAuthProviderId",
                        column: x => x.OAuthProviderId,
                        principalTable: "OAuthProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserOAuths_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    RoleId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    DepartmentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    AssigneeUserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositoryAssignments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepositoryAssignments_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepositoryAssignments_Users_AssigneeUserId",
                        column: x => x.AssigneeUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryBranches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    BranchName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LastCommitId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    LastProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositoryBranches_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryProcessingLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Step = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    IsAiOutput = table.Column<bool>(type: "boolean", nullable: false),
                    ToolName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryProcessingLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositoryProcessingLogs_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TokenUsages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    UserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    ModelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TokenUsages_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TokenUsages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserActivities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    ActivityType = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    Duration = table.Column<int>(type: "integer", nullable: true),
                    SearchQuery = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserActivities_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserActivities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserBookmarks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBookmarks_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBookmarks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserDislikes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDislikes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDislikes_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserDislikes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSubscriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BranchLanguages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RepositoryBranchId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UpdateSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    MindMapContent = table.Column<string>(type: "text", nullable: true),
                    MindMapStatus = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchLanguages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchLanguages_RepositoryBranches_RepositoryBranchId",
                        column: x => x.RepositoryBranchId,
                        principalTable: "RepositoryBranches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncrementalUpdateTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    BranchId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    PreviousCommitId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    TargetCommitId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsManualTrigger = table.Column<bool>(type: "boolean", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncrementalUpdateTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncrementalUpdateTasks_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IncrementalUpdateTasks_RepositoryBranches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "RepositoryBranches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocFiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BranchLanguageId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SourceFiles = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocFiles_BranchLanguages_BranchLanguageId",
                        column: x => x.BranchLanguageId,
                        principalTable: "BranchLanguages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TranslationTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RepositoryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    RepositoryBranchId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    SourceBranchLanguageId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    TargetLanguageCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    MaxRetryCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranslationTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TranslationTasks_BranchLanguages_SourceBranchLanguageId",
                        column: x => x.SourceBranchLanguageId,
                        principalTable: "BranchLanguages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TranslationTasks_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TranslationTasks_RepositoryBranches_RepositoryBranchId",
                        column: x => x.RepositoryBranchId,
                        principalTable: "RepositoryBranches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocCatalogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    BranchLanguageId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ParentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    DocFileId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocCatalogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocCatalogs_BranchLanguages_BranchLanguageId",
                        column: x => x.BranchLanguageId,
                        principalTable: "BranchLanguages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocCatalogs_DocCatalogs_ParentId",
                        column: x => x.ParentId,
                        principalTable: "DocCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocCatalogs_DocFiles_DocFileId",
                        column: x => x.DocFileId,
                        principalTable: "DocFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppStatistics_AppId_Date",
                table: "AppStatistics",
                columns: new[] { "AppId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchLanguages_RepositoryBranchId_LanguageCode",
                table: "BranchLanguages",
                columns: new[] { "RepositoryBranchId", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatApps_AppId",
                table: "ChatApps",
                column: "AppId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatApps_UserId",
                table: "ChatApps",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatLogs_AppId",
                table: "ChatLogs",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatLogs_CreatedAt",
                table: "ChatLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageHistories_SessionId_MessageTimestamp",
                table: "ChatMessageHistories",
                columns: new[] { "SessionId", "MessageTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageQueues_Platform_TargetUserId",
                table: "ChatMessageQueues",
                columns: new[] { "Platform", "TargetUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageQueues_Status_ScheduledAt",
                table: "ChatMessageQueues",
                columns: new[] { "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatProviderConfigs_Platform",
                table: "ChatProviderConfigs",
                column: "Platform",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_State",
                table: "ChatSessions",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId_Platform",
                table: "ChatSessions",
                columns: new[] { "UserId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatShareSnapshots_ExpiresAt",
                table: "ChatShareSnapshots",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatShareSnapshots_ShareId",
                table: "ChatShareSnapshots",
                column: "ShareId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_ParentId",
                table: "Departments",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocCatalogs_BranchLanguageId_Path",
                table: "DocCatalogs",
                columns: new[] { "BranchLanguageId", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocCatalogs_DocFileId",
                table: "DocCatalogs",
                column: "DocFileId");

            migrationBuilder.CreateIndex(
                name: "IX_DocCatalogs_ParentId",
                table: "DocCatalogs",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocFiles_BranchLanguageId",
                table: "DocFiles",
                column: "BranchLanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_IncrementalUpdateTasks_BranchId",
                table: "IncrementalUpdateTasks",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_IncrementalUpdateTasks_Priority_CreatedAt",
                table: "IncrementalUpdateTasks",
                columns: new[] { "Priority", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IncrementalUpdateTasks_RepositoryId_BranchId_Status",
                table: "IncrementalUpdateTasks",
                columns: new[] { "RepositoryId", "BranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IncrementalUpdateTasks_Status",
                table: "IncrementalUpdateTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_LocalStorages_UploaderId",
                table: "LocalStorages",
                column: "UploaderId");

            migrationBuilder.CreateIndex(
                name: "IX_McpConfigs_Name",
                table: "McpConfigs",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpDailyStatistics_Date",
                table: "McpDailyStatistics",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_McpDailyStatistics_McpProviderId_Date",
                table: "McpDailyStatistics",
                columns: new[] { "McpProviderId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpProviders_IsActive",
                table: "McpProviders",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_McpProviders_Name",
                table: "McpProviders",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpProviders_SortOrder",
                table: "McpProviders",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_McpUsageLogs_CreatedAt",
                table: "McpUsageLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_McpUsageLogs_McpProviderId_CreatedAt",
                table: "McpUsageLogs",
                columns: new[] { "McpProviderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_McpUsageLogs_ResponseStatus",
                table: "McpUsageLogs",
                column: "ResponseStatus");

            migrationBuilder.CreateIndex(
                name: "IX_McpUsageLogs_ToolName",
                table: "McpUsageLogs",
                column: "ToolName");

            migrationBuilder.CreateIndex(
                name: "IX_McpUsageLogs_UserId_CreatedAt",
                table: "McpUsageLogs",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelConfigs_Name",
                table: "ModelConfigs",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_OwnerUserId_OrgName_RepoName",
                table: "Repositories",
                columns: new[] { "OwnerUserId", "OrgName", "RepoName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAssignments_AssigneeUserId",
                table: "RepositoryAssignments",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAssignments_DepartmentId",
                table: "RepositoryAssignments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAssignments_RepositoryId",
                table: "RepositoryAssignments",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryBranches_RepositoryId_BranchName",
                table: "RepositoryBranches",
                columns: new[] { "RepositoryId", "BranchName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryProcessingLogs_RepositoryId_CreatedAt",
                table: "RepositoryProcessingLogs",
                columns: new[] { "RepositoryId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SkillConfigs_Name",
                table: "SkillConfigs",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_RecordedAt",
                table: "TokenUsages",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_RepositoryId",
                table: "TokenUsages",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_UserId",
                table: "TokenUsages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationTasks_RepositoryBranchId_TargetLanguageCode",
                table: "TranslationTasks",
                columns: new[] { "RepositoryBranchId", "TargetLanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TranslationTasks_RepositoryId",
                table: "TranslationTasks",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationTasks_SourceBranchLanguageId",
                table: "TranslationTasks",
                column: "SourceBranchLanguageId");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationTasks_Status",
                table: "TranslationTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_UserActivities_RepositoryId",
                table: "UserActivities",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserActivities_UserId_CreatedAt",
                table: "UserActivities",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserBookmarks_RepositoryId",
                table: "UserBookmarks",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBookmarks_UserId_RepositoryId",
                table: "UserBookmarks",
                columns: new[] { "UserId", "RepositoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDepartments_DepartmentId",
                table: "UserDepartments",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDepartments_UserId_DepartmentId",
                table: "UserDepartments",
                columns: new[] { "UserId", "DepartmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDislikes_RepositoryId",
                table: "UserDislikes",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDislikes_UserId_RepositoryId",
                table: "UserDislikes",
                columns: new[] { "UserId", "RepositoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserOAuths_OAuthProviderId",
                table: "UserOAuths",
                column: "OAuthProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOAuths_UserId",
                table: "UserOAuths",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferenceCaches_UserId",
                table: "UserPreferenceCaches",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId",
                table: "UserRoles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_RepositoryId",
                table: "UserSubscriptions",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId_RepositoryId",
                table: "UserSubscriptions",
                columns: new[] { "UserId", "RepositoryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppStatistics");

            migrationBuilder.DropTable(
                name: "ChatApps");

            migrationBuilder.DropTable(
                name: "ChatAssistantConfigs");

            migrationBuilder.DropTable(
                name: "ChatLogs");

            migrationBuilder.DropTable(
                name: "ChatMessageHistories");

            migrationBuilder.DropTable(
                name: "ChatMessageQueues");

            migrationBuilder.DropTable(
                name: "ChatProviderConfigs");

            migrationBuilder.DropTable(
                name: "ChatShareSnapshots");

            migrationBuilder.DropTable(
                name: "DocCatalogs");

            migrationBuilder.DropTable(
                name: "IncrementalUpdateTasks");

            migrationBuilder.DropTable(
                name: "LocalStorages");

            migrationBuilder.DropTable(
                name: "McpConfigs");

            migrationBuilder.DropTable(
                name: "McpDailyStatistics");

            migrationBuilder.DropTable(
                name: "McpProviders");

            migrationBuilder.DropTable(
                name: "McpUsageLogs");

            migrationBuilder.DropTable(
                name: "ModelConfigs");

            migrationBuilder.DropTable(
                name: "RepositoryAssignments");

            migrationBuilder.DropTable(
                name: "RepositoryProcessingLogs");

            migrationBuilder.DropTable(
                name: "SkillConfigs");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "TokenUsages");

            migrationBuilder.DropTable(
                name: "TranslationTasks");

            migrationBuilder.DropTable(
                name: "UserActivities");

            migrationBuilder.DropTable(
                name: "UserBookmarks");

            migrationBuilder.DropTable(
                name: "UserDepartments");

            migrationBuilder.DropTable(
                name: "UserDislikes");

            migrationBuilder.DropTable(
                name: "UserOAuths");

            migrationBuilder.DropTable(
                name: "UserPreferenceCaches");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "UserSubscriptions");

            migrationBuilder.DropTable(
                name: "ChatSessions");

            migrationBuilder.DropTable(
                name: "DocFiles");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "OAuthProviders");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "BranchLanguages");

            migrationBuilder.DropTable(
                name: "RepositoryBranches");

            migrationBuilder.DropTable(
                name: "Repositories");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
