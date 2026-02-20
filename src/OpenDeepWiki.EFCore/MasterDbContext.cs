using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Entities.Tools;

namespace OpenDeepWiki.EFCore;

public interface IContext : IDisposable
{
    DbSet<User> Users { get; set; }
    DbSet<Role> Roles { get; set; }
    DbSet<UserRole> UserRoles { get; set; }
    DbSet<OAuthProvider> OAuthProviders { get; set; }
    DbSet<UserOAuth> UserOAuths { get; set; }
    DbSet<LocalStorage> LocalStorages { get; set; }
    DbSet<Department> Departments { get; set; }
    DbSet<Repository> Repositories { get; set; }
    DbSet<RepositoryBranch> RepositoryBranches { get; set; }
    DbSet<BranchLanguage> BranchLanguages { get; set; }
    DbSet<DocFile> DocFiles { get; set; }
    DbSet<DocCatalog> DocCatalogs { get; set; }
    DbSet<RepositoryAssignment> RepositoryAssignments { get; set; }
    DbSet<UserBookmark> UserBookmarks { get; set; }
    DbSet<UserSubscription> UserSubscriptions { get; set; }
    DbSet<RepositoryProcessingLog> RepositoryProcessingLogs { get; set; }
    DbSet<TokenUsage> TokenUsages { get; set; }
    DbSet<SystemSetting> SystemSettings { get; set; }
    DbSet<McpConfig> McpConfigs { get; set; }
    DbSet<SkillConfig> SkillConfigs { get; set; }
    DbSet<ModelConfig> ModelConfigs { get; set; }
    DbSet<ChatSession> ChatSessions { get; set; }
    DbSet<ChatMessageHistory> ChatMessageHistories { get; set; }
    DbSet<ChatShareSnapshot> ChatShareSnapshots { get; set; }
    DbSet<ChatProviderConfig> ChatProviderConfigs { get; set; }
    DbSet<ChatMessageQueue> ChatMessageQueues { get; set; }
    DbSet<UserDepartment> UserDepartments { get; set; }
    DbSet<UserActivity> UserActivities { get; set; }
    DbSet<UserPreferenceCache> UserPreferenceCaches { get; set; }
    DbSet<UserDislike> UserDislikes { get; set; }
    DbSet<ChatAssistantConfig> ChatAssistantConfigs { get; set; }
    DbSet<ChatApp> ChatApps { get; set; }
    DbSet<AppStatistics> AppStatistics { get; set; }
    DbSet<ChatLog> ChatLogs { get; set; }
    DbSet<TranslationTask> TranslationTasks { get; set; }
    DbSet<IncrementalUpdateTask> IncrementalUpdateTasks { get; set; }
    DbSet<GitHubAppInstallation> GitHubAppInstallations { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public abstract class MasterDbContext : DbContext, IContext
{
    protected MasterDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<UserRole> UserRoles { get; set; } = null!;
    public DbSet<OAuthProvider> OAuthProviders { get; set; } = null!;
    public DbSet<UserOAuth> UserOAuths { get; set; } = null!;
    public DbSet<LocalStorage> LocalStorages { get; set; } = null!;
    public DbSet<Department> Departments { get; set; } = null!;
    public DbSet<Repository> Repositories { get; set; } = null!;
    public DbSet<RepositoryBranch> RepositoryBranches { get; set; } = null!;
    public DbSet<BranchLanguage> BranchLanguages { get; set; } = null!;
    public DbSet<DocFile> DocFiles { get; set; } = null!;
    public DbSet<DocCatalog> DocCatalogs { get; set; } = null!;
    public DbSet<RepositoryAssignment> RepositoryAssignments { get; set; } = null!;
    public DbSet<UserBookmark> UserBookmarks { get; set; } = null!;
    public DbSet<UserSubscription> UserSubscriptions { get; set; } = null!;
    public DbSet<RepositoryProcessingLog> RepositoryProcessingLogs { get; set; } = null!;
    public DbSet<TokenUsage> TokenUsages { get; set; } = null!;
    public DbSet<SystemSetting> SystemSettings { get; set; } = null!;
    public DbSet<McpConfig> McpConfigs { get; set; } = null!;
    public DbSet<SkillConfig> SkillConfigs { get; set; } = null!;
    public DbSet<ModelConfig> ModelConfigs { get; set; } = null!;
    public DbSet<ChatSession> ChatSessions { get; set; } = null!;
    public DbSet<ChatMessageHistory> ChatMessageHistories { get; set; } = null!;
    public DbSet<ChatShareSnapshot> ChatShareSnapshots { get; set; } = null!;
    public DbSet<ChatProviderConfig> ChatProviderConfigs { get; set; } = null!;
    public DbSet<ChatMessageQueue> ChatMessageQueues { get; set; } = null!;
    public DbSet<UserDepartment> UserDepartments { get; set; } = null!;
    public DbSet<UserActivity> UserActivities { get; set; } = null!;
    public DbSet<UserPreferenceCache> UserPreferenceCaches { get; set; } = null!;
    public DbSet<UserDislike> UserDislikes { get; set; } = null!;
    public DbSet<ChatAssistantConfig> ChatAssistantConfigs { get; set; } = null!;
    public DbSet<ChatApp> ChatApps { get; set; } = null!;
    public DbSet<AppStatistics> AppStatistics { get; set; } = null!;
    public DbSet<ChatLog> ChatLogs { get; set; } = null!;
    public DbSet<TranslationTask> TranslationTasks { get; set; } = null!;
    public DbSet<IncrementalUpdateTask> IncrementalUpdateTasks { get; set; } = null!;
    public DbSet<GitHubAppInstallation> GitHubAppInstallations { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Department>()
            .HasOne(department => department.Parent)
            .WithMany()
            .HasForeignKey(department => department.ParentId);

        modelBuilder.Entity<Repository>()
            .HasIndex(repository => new { repository.OwnerUserId, repository.OrgName, repository.RepoName })
            .IsUnique();

        modelBuilder.Entity<RepositoryBranch>()
            .HasIndex(branch => new { branch.RepositoryId, branch.BranchName })
            .IsUnique();

        modelBuilder.Entity<BranchLanguage>()
            .HasIndex(language => new { language.RepositoryBranchId, language.LanguageCode })
            .IsUnique();

        // DocCatalog 树形结构配置
        modelBuilder.Entity<DocCatalog>()
            .HasOne(catalog => catalog.Parent)
            .WithMany(catalog => catalog.Children)
            .HasForeignKey(catalog => catalog.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // DocCatalog 路径唯一索引（同一分支语言下路径唯一）
        modelBuilder.Entity<DocCatalog>()
            .HasIndex(catalog => new { catalog.BranchLanguageId, catalog.Path })
            .IsUnique();

        // DocCatalog 与 DocFile 关联
        modelBuilder.Entity<DocCatalog>()
            .HasOne(catalog => catalog.DocFile)
            .WithMany()
            .HasForeignKey(catalog => catalog.DocFileId)
            .OnDelete(DeleteBehavior.SetNull);

        // UserBookmark 唯一索引（同一用户对同一仓库只能收藏一次）
        modelBuilder.Entity<UserBookmark>()
            .HasIndex(b => new { b.UserId, b.RepositoryId })
            .IsUnique();

        // UserSubscription 唯一索引（同一用户对同一仓库只能订阅一次）
        modelBuilder.Entity<UserSubscription>()
            .HasIndex(s => new { s.UserId, s.RepositoryId })
            .IsUnique();

        // RepositoryProcessingLog 索引（按仓库ID和创建时间查询）
        modelBuilder.Entity<RepositoryProcessingLog>()
            .HasIndex(log => new { log.RepositoryId, log.CreatedAt });

        // TokenUsage 索引（按记录时间查询统计）
        modelBuilder.Entity<TokenUsage>()
            .HasIndex(t => t.RecordedAt);

        // SystemSetting 唯一键索引
        modelBuilder.Entity<SystemSetting>()
            .HasIndex(s => s.Key)
            .IsUnique();

        // McpConfig 名称唯一索引
        modelBuilder.Entity<McpConfig>()
            .HasIndex(m => m.Name)
            .IsUnique();

        // SkillConfig 名称唯一索引
        modelBuilder.Entity<SkillConfig>()
            .HasIndex(s => s.Name)
            .IsUnique();

        // ModelConfig 名称唯一索引
        modelBuilder.Entity<ModelConfig>()
            .HasIndex(m => m.Name)
            .IsUnique();

        // ChatSession 用户和平台组合唯一索引
        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => new { s.UserId, s.Platform })
            .IsUnique();

        // ChatSession 状态索引（用于查询活跃会话）
        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => s.State);

        // ChatMessageHistory 与 ChatSession 关联
        modelBuilder.Entity<ChatMessageHistory>()
            .HasOne(m => m.Session)
            .WithMany(s => s.Messages)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ChatMessageHistory 会话ID和时间戳索引（用于按时间查询消息）
        modelBuilder.Entity<ChatMessageHistory>()
            .HasIndex(m => new { m.SessionId, m.MessageTimestamp });

        // ChatShareSnapshot ShareId 唯一索引
        modelBuilder.Entity<ChatShareSnapshot>()
            .HasIndex(s => s.ShareId)
            .IsUnique();

        // ChatShareSnapshot 过期时间索引
        modelBuilder.Entity<ChatShareSnapshot>()
            .HasIndex(s => s.ExpiresAt);

        // ChatProviderConfig 平台唯一索引
        modelBuilder.Entity<ChatProviderConfig>()
            .HasIndex(c => c.Platform)
            .IsUnique();

        // ChatMessageQueue 状态和计划时间索引（用于出队处理）
        modelBuilder.Entity<ChatMessageQueue>()
            .HasIndex(q => new { q.Status, q.ScheduledAt });

        // ChatMessageQueue 平台和目标用户索引（用于按用户查询队列）
        modelBuilder.Entity<ChatMessageQueue>()
            .HasIndex(q => new { q.Platform, q.TargetUserId });

        // UserDepartment 唯一索引（同一用户在同一部门只能有一条记录）
        modelBuilder.Entity<UserDepartment>()
            .HasIndex(ud => new { ud.UserId, ud.DepartmentId })
            .IsUnique();

        // UserActivity 索引（按用户ID和时间查询）
        modelBuilder.Entity<UserActivity>()
            .HasIndex(a => new { a.UserId, a.CreatedAt });

        // UserActivity 索引（按仓库ID查询）
        modelBuilder.Entity<UserActivity>()
            .HasIndex(a => a.RepositoryId);

        // UserPreferenceCache 用户ID唯一索引
        modelBuilder.Entity<UserPreferenceCache>()
            .HasIndex(p => p.UserId)
            .IsUnique();

        // UserDislike 唯一索引（同一用户对同一仓库只能标记一次不感兴趣）
        modelBuilder.Entity<UserDislike>()
            .HasIndex(d => new { d.UserId, d.RepositoryId })
            .IsUnique();

        // ChatApp AppId唯一索引
        modelBuilder.Entity<ChatApp>()
            .HasIndex(a => a.AppId)
            .IsUnique();

        // ChatApp 用户ID索引（用于查询用户的应用列表）
        modelBuilder.Entity<ChatApp>()
            .HasIndex(a => a.UserId);

        // AppStatistics AppId和日期组合唯一索引
        modelBuilder.Entity<AppStatistics>()
            .HasIndex(s => new { s.AppId, s.Date })
            .IsUnique();

        // ChatLog AppId索引（用于按应用查询提问记录）
        modelBuilder.Entity<ChatLog>()
            .HasIndex(l => l.AppId);

        // ChatLog 创建时间索引（用于按时间范围查询）
        modelBuilder.Entity<ChatLog>()
            .HasIndex(l => l.CreatedAt);

        // TranslationTask 状态索引（用于查询待处理任务）
        modelBuilder.Entity<TranslationTask>()
            .HasIndex(t => t.Status);

        // TranslationTask 仓库分支和目标语言组合唯一索引（避免重复任务）
        modelBuilder.Entity<TranslationTask>()
            .HasIndex(t => new { t.RepositoryBranchId, t.TargetLanguageCode })
            .IsUnique();

        // IncrementalUpdateTask 状态索引（用于查询待处理任务）
        modelBuilder.Entity<IncrementalUpdateTask>()
            .HasIndex(t => t.Status);

        // IncrementalUpdateTask 仓库分支和状态组合索引（防止重复的待处理/处理中任务）
        modelBuilder.Entity<IncrementalUpdateTask>()
            .HasIndex(t => new { t.RepositoryId, t.BranchId, t.Status });

        // IncrementalUpdateTask 优先级和创建时间索引（用于按优先级排序处理）
        modelBuilder.Entity<IncrementalUpdateTask>()
            .HasIndex(t => new { t.Priority, t.CreatedAt });

        // GitHubAppInstallation unique index on InstallationId
        modelBuilder.Entity<GitHubAppInstallation>()
            .HasIndex(g => g.InstallationId)
            .IsUnique();

        // GitHubAppInstallation optional FK to Department
        modelBuilder.Entity<GitHubAppInstallation>()
            .HasOne(g => g.Department)
            .WithMany()
            .HasForeignKey(g => g.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
