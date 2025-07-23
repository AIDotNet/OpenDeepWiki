using BCrypt.Net;

namespace KoalaWiki.Services;

/// <summary>
/// 密码服务，用于密码的加密和验证
/// </summary>
public interface IPasswordService
{
    /// <summary>
    /// 哈希密码
    /// </summary>
    /// <param name="password">明文密码</param>
    /// <returns>哈希后的密码</returns>
    string HashPassword(string password);
    
    /// <summary>
    /// 验证密码
    /// </summary>
    /// <param name="password">明文密码</param>
    /// <param name="hashedPassword">哈希密码</param>
    /// <returns>验证结果</returns>
    bool VerifyPassword(string password, string hashedPassword);
    
    /// <summary>
    /// 检查密码是否为明文（用于迁移现有数据）
    /// </summary>
    /// <param name="password">密码</param>
    /// <returns>是否为明文</returns>
    bool IsPlainTextPassword(string password);
}

/// <summary>
/// 密码服务实现
/// </summary>
public class PasswordService : IPasswordService
{
    /// <summary>
    /// 哈希密码
    /// </summary>
    /// <param name="password">明文密码</param>
    /// <returns>哈希后的密码</returns>
    public string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("密码不能为空", nameof(password));
        }
        
        return BCrypt.Net.BCrypt.HashPassword(password, 12); // 使用工作因子12提供安全性和性能的平衡
    }
    
    /// <summary>
    /// 验证密码
    /// </summary>
    /// <param name="password">明文密码</param>
    /// <param name="hashedPassword">哈希密码</param>
    /// <returns>验证结果</returns>
    public bool VerifyPassword(string password, string hashedPassword)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword))
        {
            return false;
        }
        
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 检查密码是否为明文（用于迁移现有数据）
    /// 简单检查：BCrypt哈希通常以$2a$、$2b$、$2x$或$2y$开头
    /// </summary>
    /// <param name="password">密码</param>
    /// <returns>是否为明文</returns>
    public bool IsPlainTextPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return true;
        }
        
        // BCrypt哈希格式检查
        return !password.StartsWith("$2a$") && 
               !password.StartsWith("$2b$") && 
               !password.StartsWith("$2x$") && 
               !password.StartsWith("$2y$");
    }
}