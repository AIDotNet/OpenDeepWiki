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
            .HasIndex(repository => new { repository.OrgName, repository.RepoName })
            .IsUnique();

        modelBuilder.Entity<RepositoryBranch>()
            .HasIndex(branch => new { branch.RepositoryId, branch.BranchName })
            .IsUnique();

        modelBuilder.Entity<BranchLanguage>()
            .HasIndex(language => new { language.RepositoryBranchId, language.LanguageCode })
            .IsUnique();

        // DocCatalog tree structure configuration
        modelBuilder.Entity<DocCatalog>()
            .HasOne(catalog => catalog.Parent)
            .WithMany(catalog => catalog.Children)
            .HasForeignKey(catalog => catalog.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // DocCatalog path unique index (path must be unique within the same branch language)
        modelBuilder.Entity<DocCatalog>()
            .HasIndex(catalog => new { catalog.BranchLanguageId, catalog.Path })
            .IsUnique();

        // DocCatalog and DocFile association
        modelBuilder.Entity<DocCatalog>()
            .HasOne(catalog => catalog.DocFile)
            .WithMany()
            .HasForeignKey(catalog => catalog.DocFileId)
            .OnDelete(DeleteBehavior.SetNull);

        // UserBookmark unique index (a user can only bookmark the same repository once)
        modelBuilder.Entity<UserBookmark>()
            .HasIndex(b => new { b.UserId, b.RepositoryId })
            .IsUnique();

        // UserSubscription unique index (a user can only subscribe to the same repository once)
        modelBuilder.Entity<UserSubscription>()
            .HasIndex(s => new { s.UserId, s.RepositoryId })
            .IsUnique();

        // RepositoryProcessingLog index (query by repository ID and creation time)
        modelBuilder.Entity<RepositoryProcessingLog>()
            .HasIndex(log => new { log.RepositoryId, log.CreatedAt });

        // TokenUsage index (query statistics by record time)
        modelBuilder.Entity<TokenUsage>()
            .HasIndex(t => t.RecordedAt);

        // SystemSetting unique key index
        modelBuilder.Entity<SystemSetting>()
            .HasIndex(s => s.Key)
            .IsUnique();

        // McpConfig name unique index
        modelBuilder.Entity<McpConfig>()
            .HasIndex(m => m.Name)
            .IsUnique();

        // SkillConfig name unique index
        modelBuilder.Entity<SkillConfig>()
            .HasIndex(s => s.Name)
            .IsUnique();

        // ModelConfig name unique index
        modelBuilder.Entity<ModelConfig>()
            .HasIndex(m => m.Name)
            .IsUnique();

        // ChatSession user and platform composite unique index
        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => new { s.UserId, s.Platform })
            .IsUnique();

        // ChatSession state index (for querying active sessions)
        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => s.State);

        // ChatMessageHistory and ChatSession association
        modelBuilder.Entity<ChatMessageHistory>()
            .HasOne(m => m.Session)
            .WithMany(s => s.Messages)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ChatMessageHistory session ID and timestamp index (for querying messages by time)
        modelBuilder.Entity<ChatMessageHistory>()
            .HasIndex(m => new { m.SessionId, m.MessageTimestamp });

        // ChatShareSnapshot ShareId unique index
        modelBuilder.Entity<ChatShareSnapshot>()
            .HasIndex(s => s.ShareId)
            .IsUnique();

        // ChatShareSnapshot expiration time index
        modelBuilder.Entity<ChatShareSnapshot>()
            .HasIndex(s => s.ExpiresAt);

        // ChatProviderConfig platform unique index
        modelBuilder.Entity<ChatProviderConfig>()
            .HasIndex(c => c.Platform)
            .IsUnique();

        // ChatMessageQueue status and scheduled time index (for dequeue processing)
        modelBuilder.Entity<ChatMessageQueue>()
            .HasIndex(q => new { q.Status, q.ScheduledAt });

        // ChatMessageQueue platform and target user index (for querying queue by user)
        modelBuilder.Entity<ChatMessageQueue>()
            .HasIndex(q => new { q.Platform, q.TargetUserId });

        // UserDepartment unique index (a user can only have one record in the same department)
        modelBuilder.Entity<UserDepartment>()
            .HasIndex(ud => new { ud.UserId, ud.DepartmentId })
            .IsUnique();

        // UserActivity index (query by user ID and time)
        modelBuilder.Entity<UserActivity>()
            .HasIndex(a => new { a.UserId, a.CreatedAt });

        // UserActivity index (query by repository ID)
        modelBuilder.Entity<UserActivity>()
            .HasIndex(a => a.RepositoryId);

        // UserPreferenceCache user ID unique index
        modelBuilder.Entity<UserPreferenceCache>()
            .HasIndex(p => p.UserId)
            .IsUnique();

        // UserDislike unique index (a user can only mark the same repository as not interested once)
        modelBuilder.Entity<UserDislike>()
            .HasIndex(d => new { d.UserId, d.RepositoryId })
            .IsUnique();

        // ChatApp AppId unique index
        modelBuilder.Entity<ChatApp>()
            .HasIndex(a => a.AppId)
            .IsUnique();

        // ChatApp user ID index (for querying user's application list)
        modelBuilder.Entity<ChatApp>()
            .HasIndex(a => a.UserId);

        // AppStatistics AppId and date composite unique index
        modelBuilder.Entity<AppStatistics>()
            .HasIndex(s => new { s.AppId, s.Date })
            .IsUnique();

        // ChatLog AppId index (for querying question records by application)
        modelBuilder.Entity<ChatLog>()
            .HasIndex(l => l.AppId);

        // ChatLog creation time index (for querying by time range)
        modelBuilder.Entity<ChatLog>()
            .HasIndex(l => l.CreatedAt);

        // TranslationTask status index (for querying pending tasks)
        modelBuilder.Entity<TranslationTask>()
            .HasIndex(t => t.Status);

        // TranslationTask repository branch and target language composite unique index (to avoid duplicate tasks)
        modelBuilder.Entity<TranslationTask>()
            .HasIndex(t => new { t.RepositoryBranchId, t.TargetLanguageCode })
            .IsUnique();

        // IncrementalUpdateTask status index (for querying pending tasks)
        modelBuilder.Entity<IncrementalUpdateTask>()
            .HasIndex(t => t.Status);

        // IncrementalUpdateTask repository branch and status composite index (to prevent duplicate pending/processing tasks)
        modelBuilder.Entity<IncrementalUpdateTask>()
            .HasIndex(t => new { t.RepositoryId, t.BranchId, t.Status });

        // IncrementalUpdateTask priority and creation time index (for processing sorted by priority)
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
