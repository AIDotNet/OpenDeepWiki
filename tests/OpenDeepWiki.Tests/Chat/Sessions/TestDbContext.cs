using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Entities.Tools;

namespace OpenDeepWiki.Tests.Chat.Sessions;

/// <summary>
/// 测试用内存数据库上下文
/// </summary>
public class TestDbContext : DbContext, IContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
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
    public DbSet<McpProvider> McpProviders { get; set; }
    public DbSet<McpUsageLog> McpUsageLogs { get; set; }
    public DbSet<McpDailyStatistics> McpDailyStatistics { get; set; }
    public DbSet<ChatShareSnapshot> ChatShareSnapshots { get; set; } = default!;
    

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // ChatSession 用户和平台组合唯一索引
        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => new { s.UserId, s.Platform })
            .IsUnique();
        
        // ChatMessageHistory 与 ChatSession 关联
        modelBuilder.Entity<ChatMessageHistory>()
            .HasOne(m => m.Session)
            .WithMany(s => s.Messages)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
    
    /// <summary>
    /// 创建新的测试数据库上下文
    /// </summary>
    public static TestDbContext Create()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        var context = new TestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
