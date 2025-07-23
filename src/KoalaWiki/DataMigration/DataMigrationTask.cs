using KoalaWiki.Core.DataAccess;
using KoalaWiki.Domains.Users;
using KoalaWiki.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KoalaWiki.DataMigration;

public class DataMigrationTask(IServiceProvider service, ILogger<DataMigrationTask> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1000, stoppingToken);

        await using var scope = service.CreateAsyncScope();

        var dbContext = scope.ServiceProvider.GetService<IKoalaWikiContext>();
        var passwordService = scope.ServiceProvider.GetService<IPasswordService>();


        await InitializeUsersAsync(dbContext, passwordService, stoppingToken);
        await MigrateExistingPasswordsAsync(dbContext, passwordService, stoppingToken);
    }

    /// <summary>
    /// 用户初始化任务
    /// </summary>
    private async Task InitializeUsersAsync(IKoalaWikiContext dbContext, IPasswordService passwordService, CancellationToken stoppingToken)
    {
        // 判断是否存在账号
        if (await dbContext.UserInRoles.AnyAsync(cancellationToken: stoppingToken))
        {
            // 如果存在账号则不进行初始化
            return;
        }

        // 迁移数据库
        var adminRole = new Role
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "admin",
            Description = "管理员角色",
            CreatedAt = DateTime.UtcNow,
            IsSystemRole = true,
            UpdatedAt = DateTime.UtcNow,
        };

        var userRole = new Role
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "user",
            Description = "普通用户角色",
            IsSystemRole = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        // 创建管理员角色
        await dbContext.Roles.AddRangeAsync(adminRole, userRole);

        if (await dbContext.Users.AnyAsync(cancellationToken: stoppingToken))
        {
            // 给所有用户初始化角色
            var users = await dbContext.Users.ToListAsync(cancellationToken: stoppingToken);
            foreach (var user in users)
            {
                if (user.Name == "admin")
                {
                    await dbContext.UserInRoles.AddAsync(new UserInRole
                    {
                        UserId = user.Id,
                        RoleId = adminRole.Id
                    }, stoppingToken);
                }
                else
                {
                    await dbContext.UserInRoles.AddAsync(new UserInRole
                    {
                        UserId = user.Id,
                        RoleId = userRole.Id
                    }, stoppingToken);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken: stoppingToken);

            return;
        }

        // 迁移数据库
        var admin = new User
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "admin",
            Password = passwordService?.HashPassword("admin") ?? "admin", // 使用哈希密码，如果服务不可用则回退到明文
            Email = "239573049@qq.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Avatar = "https://avatars.githubusercontent.com/u/61819790?v=4",
        };

        // 创建管理员账号
        await dbContext.Users.AddAsync(admin, stoppingToken);

        await dbContext.UserInRoles.AddAsync(new UserInRole()
        {
            UserId = admin.Id,
            RoleId = adminRole.Id
        }, stoppingToken);

        await dbContext.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    /// 迁移现有明文密码为哈希密码
    /// </summary>
    private async Task MigrateExistingPasswordsAsync(IKoalaWikiContext dbContext, IPasswordService passwordService, CancellationToken stoppingToken)
    {
        if (passwordService == null)
        {
            logger.LogWarning("密码服务不可用，跳过现有密码迁移");
            return;
        }

        logger.LogInformation("开始检查现有用户密码迁移需求...");

        // 查找所有明文密码的用户
        var usersWithPlaintextPasswords = await dbContext.Users
            .Where(u => !string.IsNullOrEmpty(u.Password))
            .ToListAsync(stoppingToken);

        var migratedCount = 0;
        var totalUsers = usersWithPlaintextPasswords.Count;

        foreach (var user in usersWithPlaintextPasswords)
        {
            // 检查是否为明文密码
            if (passwordService.IsPlainTextPassword(user.Password))
            {
                var originalPassword = user.Password;
                
                // 将明文密码哈希化
                user.Password = passwordService.HashPassword(originalPassword);
                user.UpdatedAt = DateTime.UtcNow;
                
                migratedCount++;
                
                // 不记录具体密码内容，只记录用户ID和用户名
                logger.LogInformation("用户 {UserId} ({UserName}) 的密码已从明文迁移到哈希", user.Id, user.Name);
            }
        }

        if (migratedCount > 0)
        {
            // 批量保存所有更改
            await dbContext.SaveChangesAsync(stoppingToken);
            logger.LogInformation("密码迁移完成！总共迁移了 {MigratedCount}/{TotalUsers} 个用户的密码", migratedCount, totalUsers);
        }
        else
        {
            logger.LogInformation("密码迁移检查完成，{TotalUsers} 个用户中没有发现明文密码", totalUsers);
        }
    }
}