using KoalaWiki.Core.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace KoalaWiki.Provider.MySQL;

public class MySQLContext(DbContextOptions<MySQLContext> options)
    : KoalaWikiContext<MySQLContext>(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure MySQL specific type mappings to avoid Int64/Int32 casting issues
        modelBuilder.Entity<KoalaWiki.Domains.Document>(entity =>
        {
            entity.Property(e => e.LikeCount).HasColumnType("bigint");
            entity.Property(e => e.CommentCount).HasColumnType("bigint");
        });
        
        modelBuilder.Entity<KoalaWiki.Domains.DocumentFile.DocumentFileItem>(entity =>
        {
            entity.Property(e => e.CommentCount).HasColumnType("bigint");
            entity.Property(e => e.Size).HasColumnType("bigint");
        });
    }
}