using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FastService;
using KoalaWiki.Domains.Users;
using KoalaWiki.Dto;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Octokit;
using User = KoalaWiki.Domains.Users.User;

namespace KoalaWiki.Services;

/// <summary>
/// 认证服务
/// </summary>
[Tags("认证服务")]
[Route("/api/Auth")]
[Filter(typeof(ResultFilter))]
public class AuthService(
    IKoalaWikiContext dbContext,
    JwtOptions jwtOptions,
    IMapper mapper,
    ILogger<AuthService> logger,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
    IPasswordService passwordService) : FastApi
{
    /// <summary>
    /// 用户登录
    /// </summary>
    /// <param name="username">用户名</param>
    /// <param name="password">密码</param>
    /// <returns>登录结果</returns>
    public async Task<LoginDto> LoginAsync(
        string username, string password)
    {
        try
        {
            // 查询用户
            var user = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Name == username || u.Email == username);

            // 用户不存在
            if (user == null)
            {
                return new LoginDto(false, string.Empty, null, null, "用户不存在");
            }

            // 验证密码
            bool passwordValid = false;
            bool needsPasswordMigration = false;
            
            // 检查是否为明文密码（用于迁移现有数据）
            if (passwordService.IsPlainTextPassword(user.Password))
            {
                // 明文密码比较
                passwordValid = password == user.Password;
                needsPasswordMigration = passwordValid; // 如果密码正确，需要迁移到哈希
            }
            else
            {
                // 哈希密码验证
                passwordValid = passwordService.VerifyPassword(password, user.Password);
            }
            
            if (!passwordValid)
            {
                return new LoginDto(false, string.Empty, null, null, "密码错误");
            }
            
            // 如果需要迁移密码，将明文密码更新为哈希密码
            if (needsPasswordMigration)
            {
                user.Password = passwordService.HashPassword(password);
                logger.LogInformation("用户 {UserId} 的密码已从明文迁移到哈希", user.Id);
            }

            // 更新登录信息
            user.LastLoginAt = DateTime.UtcNow;
            user.LastLoginIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
            dbContext.Users.Update(user);
            await dbContext.SaveChangesAsync();

            // 获取当前胡的角色
            var roleIds = await dbContext.UserInRoles
                .Where(ur => ur.UserId == user.Id)
                .Select(x => x.RoleId)
                .ToListAsync();

            var roles = await dbContext.Roles
                .Where(r => roleIds.Contains(r.Id))
                .ToListAsync();

            user.Password = string.Empty; // 清空密码
            var dto = mapper.Map<UserInfoDto>(user);
            dto.Role = string.Join(',', roles.Select(x => x.Name));

            // 生成JWT令牌
            var token = GenerateJwtToken(user, roles);
            var refreshToken = GenerateRefreshToken(user);

            // 设置到cookie
            var tokenCookieOptions = CreateCookieOptions(jwtOptions.ExpireMinutes);
            var refreshTokenCookieOptions = CreateCookieOptions(jwtOptions.RefreshExpireMinutes);

            httpContextAccessor.HttpContext?.Response.Cookies.Append("refreshToken", refreshToken,
                refreshTokenCookieOptions);
            httpContextAccessor.HttpContext?.Response.Cookies.Append("token", token, tokenCookieOptions);

            logger.LogInformation(
                "用户登录成功，已设置cookie。Token长度: {TokenLength}, 环境: {Environment}, HTTPS: {IsHttps}, Secure: {Secure}, SameSite: {SameSite}",
                token.Length,
                configuration.GetValue<string>("ASPNETCORE_ENVIRONMENT"),
                httpContextAccessor.HttpContext?.Request.IsHttps,
                tokenCookieOptions.Secure,
                tokenCookieOptions.SameSite);

            return new LoginDto(true, token, refreshToken, dto, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "用户登录失败");
            return new LoginDto(false, string.Empty, null, null, "登录失败，请稍后再试");
        }
    }

    /// <summary>
    /// 用户注册
    /// </summary>
    /// <returns>注册结果</returns>
    public async Task<LoginDto> RegisterAsync(RegisterInput input)
    {
        try
        {
            // 验证用户名格式
            if (!Regex.IsMatch(input.UserName, @"^[a-zA-Z0-9_-]{3,16}$"))
            {
                throw new ArgumentException("用户名格式不正确，必须是3-16位的字母、数字、下划线或连字符");
            }

            // 验证邮箱格式
            if (!Regex.IsMatch(input.Email, @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$"))
            {
                throw new ArgumentException("邮箱格式不正确");
            }

            // 验证密码强度
            if (input.Password.Length < 6)
            {
                throw new ArgumentException("密码长度不能小于6位");
            }

            // 检查用户名是否已存在
            var existingUsername = await dbContext.Users.AnyAsync(u => u.Name == input.UserName);
            if (existingUsername)
            {
                throw new ArgumentException("用户名已存在");
            }

            // 检查邮箱是否已存在
            var existingEmail = await dbContext.Users.AnyAsync(u => u.Email == input.Email);
            if (existingEmail)
            {
                throw new ArgumentException("邮箱已被注册");
            }

            // 创建用户
            var user = new User
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = input.UserName,
                Email = input.Email,
                Password = passwordService.HashPassword(input.Password), // 哈希密码
                CreatedAt = DateTime.UtcNow,
            };

            // 保存用户
            await dbContext.Users.AddAsync(user);
            await dbContext.SaveChangesAsync();
            // 获取当前胡的角色
            var roleIds = await dbContext.UserInRoles
                .Where(ur => ur.UserId == user.Id)
                .Select(x => x.RoleId)
                .ToListAsync();

            var roles = await dbContext.Roles
                .Where(r => roleIds.Contains(r.Id))
                .ToListAsync();

            user.Password = string.Empty; // 清空密码

            var dto = mapper.Map<UserInfoDto>(user);

            // 创建token
            var token = GenerateJwtToken(user, roles);
            var refreshToken = GenerateRefreshToken(user);

            // 设置到cookie
            httpContextAccessor.HttpContext?.Response.Cookies.Append("refreshToken", refreshToken,
                CreateCookieOptions(jwtOptions.RefreshExpireMinutes));
            httpContextAccessor.HttpContext?.Response.Cookies.Append("token", token,
                CreateCookieOptions(jwtOptions.ExpireMinutes));
            return new LoginDto(true, token, refreshToken, dto, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "用户注册失败");
            throw;
        }
    }

    /// <summary>
    /// GitHub登录
    /// </summary>
    /// <param name="code">授权码</param>
    /// <returns>登录结果</returns>
    public async Task<LoginDto>
        GitHubLoginAsync(string code)
    {
        try
        {
            var clientId = configuration["GitHub:ClientId"];
            var clientSecret = configuration["GitHub:ClientSecret"];

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                return new LoginDto(false, string.Empty, null, null, "GitHub配置错误");
            }

            // 获取访问令牌
            var client = new GitHubClient(new ProductHeaderValue("KoalaWiki"));
            var request = new OauthTokenRequest(clientId, clientSecret, code);
            var token = await client.Oauth.CreateAccessToken(request);

            if (string.IsNullOrEmpty(token.AccessToken))
            {
                return new LoginDto(false, string.Empty, null, null, "GitHub授权失败");
            }

            // 设置访问令牌
            client.Credentials = new Credentials(token.AccessToken);

            // 获取用户信息
            var githubUser = await client.User.Current();

            // 查询用户是否存在
            var userInAuth = await dbContext.UserInAuths.FirstOrDefaultAsync(u =>
                u.Id == githubUser.Id.ToString() && u.Provider == "GitHub");

            User user = null;
            if (userInAuth != null)
            {
                user = await dbContext.Users
                    .FirstOrDefaultAsync(u => u.Id == userInAuth.UserId);
            }

            // 用户不存在，自动注册
            if (user == null)
            {
                user = new User
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = githubUser.Login,
                    Email = githubUser.Email ?? string.Empty,
                    Password = passwordService.HashPassword(Guid.NewGuid().ToString()), // 哈希随机密码
                    Avatar = githubUser.AvatarUrl,
                    CreatedAt = DateTime.UtcNow,
                };

                // 绑定GitHub账号
                userInAuth = new UserInAuth
                {
                    Id = githubUser.Id.ToString(),
                    UserId = user.Id,
                    Provider = "GitHub",
                    CreatedAt = DateTime.UtcNow
                };

                // 获取普通用户角色
                var userRole = await dbContext.Roles
                    .FirstOrDefaultAsync(r => r.Name == "user");

                await dbContext.UserInRoles.AddAsync(new UserInRole
                {
                    UserId = user.Id,
                    RoleId = userRole!.Id
                });

                // 保存用户
                await dbContext.Users.AddAsync(user);
                await dbContext.UserInAuths.AddAsync(userInAuth);
                await dbContext.SaveChangesAsync();
            }
            else if (user == null)
            {
                return new LoginDto(false, string.Empty, null, null, "GitHub账号未绑定邮箱，无法登录");
            }

            // 更新登录信息
            user.LastLoginAt = DateTime.UtcNow;
            user.LastLoginIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

            if (httpContextAccessor.HttpContext?.Request.Headers["x-forwarded-for"].Count > 0)
            {
                user.LastLoginIp = httpContextAccessor.HttpContext.Request.Headers["x-forwarded-for"];
            }
            else if (httpContextAccessor.HttpContext?.Request.Headers["x-real-ip"].Count > 0)
            {
                user.LastLoginIp = httpContextAccessor.HttpContext.Request.Headers["x-real-ip"];
            }

            await dbContext.SaveChangesAsync();

            // 获取当前胡的角色
            var roleIds = await dbContext.UserInRoles
                .Where(ur => ur.UserId == user.Id)
                .Select(x => x.RoleId)
                .ToListAsync();

            var roles = await dbContext.Roles
                .Where(r => roleIds.Contains(r.Id))
                .ToListAsync();

            // 生成JWT令牌
            var jwtToken = GenerateJwtToken(user, roles);
            var refreshToken = GenerateRefreshToken(user);

            var userDto = mapper.Map<UserInfoDto>(user);

            userDto.Role = string.Join(',', roles.Select(x => x.Name));


            // 设置到cookie
            httpContextAccessor.HttpContext?.Response.Cookies.Append("refreshToken", refreshToken,
                CreateCookieOptions(jwtOptions.RefreshExpireMinutes));
            httpContextAccessor.HttpContext?.Response.Cookies.Append("token", jwtToken,
                CreateCookieOptions(jwtOptions.ExpireMinutes));

            return new LoginDto(true, jwtToken, refreshToken, userDto, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GitHub登录失败");
            return new LoginDto(false, string.Empty, null, null, "GitHub登录失败，请稍后再试");
        }
    }

    // /// <summary>
    // /// 谷歌邮箱登录
    // /// </summary>
    // /// <param name="idToken">ID令牌</param>
    // /// <returns>登录结果</returns>
    // public async Task<(bool Success, string Token, string? RefreshToken, User? User, string? ErrorMessage)>
    //     GoogleLoginAsync(string idToken)
    // {
    //     try
    //     {
    //         // 验证Google ID令牌
    //         var payload = await ValidateGoogleIdToken(idToken);
    //         if (payload == null)
    //         {
    //             return (false, string.Empty, null, null, "Google认证失败");
    //         }
    //
    //         // 获取邮箱
    //         var email = payload.Email;
    //         if (string.IsNullOrEmpty(email))
    //         {
    //             return (false, string.Empty, null, null, "无法获取Google邮箱");
    //         }
    //
    //         // 查询用户是否存在
    //         var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
    //
    //         // 用户不存在，自动注册
    //         if (user == null)
    //         {
    //             // 生成用户名
    //             var username = email.Split('@')[0];
    //             var existingUsername = await dbContext.Users.AnyAsync(u => u.Name == username);
    //             if (existingUsername)
    //             {
    //                 username = $"{username}{Guid.NewGuid().ToString("N").Substring(0, 6)}";
    //             }
    //
    //             user = new User
    //             {
    //                 Id = Guid.NewGuid().ToString("N"),
    //                 Name = username,
    //                 Email = email,
    //                 Password = Guid.NewGuid().ToString(), // 随机密码
    //                 Avatar = payload.Picture,
    //                 CreatedAt = DateTime.UtcNow,
    //                 Role = "user" // 默认角色
    //             };
    //
    //             // 保存用户
    //             await dbContext.Users.AddAsync(user);
    //             await dbContext.SaveChangesAsync();
    //         }
    //
    //         // 更新登录信息
    //         user.LastLoginAt = DateTime.UtcNow;
    //         user.LastLoginIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    //         await dbContext.SaveChangesAsync();
    //
    //         // 生成JWT令牌
    //         var token = GenerateJwtToken(user);
    //         var refreshToken = GenerateRefreshToken(user);
    //
    //         return (true, token, refreshToken, user, null);
    //     }
    //     catch (Exception ex)
    //     {
    //         logger.LogError(ex, "Google登录失败");
    //         return (false, string.Empty, null, null, "Google登录失败，请稍后再试");
    //     }
    // }

    /// <summary>
    /// 用户退出登录
    /// </summary>
    /// <returns>退出结果</returns>
    public async Task<bool> LogoutAsync()
    {
        try
        {
            // 清除cookie
            httpContextAccessor.HttpContext?.Response.Cookies.Delete("token");
            httpContextAccessor.HttpContext?.Response.Cookies.Delete("refreshToken");

            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "用户退出登录失败");
            return false;
        }
    }

    /// <summary>
    /// 刷新令牌
    /// </summary>
    /// <param name="refreshToken">刷新令牌</param>
    /// <returns>刷新结果</returns>
    public async Task<(bool Success, string Token, string? RefreshToken, string? ErrorMessage)> RefreshTokenAsync(
        string refreshToken)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = jwtOptions.GetSymmetricSecurityKey();

            // 验证刷新令牌
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            try
            {
                var principal = tokenHandler.ValidateToken(refreshToken, validationParameters, out _);
                var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

                // 查询用户
                var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    return (false, string.Empty, null, "用户不存在");
                }

                // 获取当前胡的角色
                var roleIds = await dbContext.UserInRoles
                    .Where(ur => ur.UserId == user.Id)
                    .Select(x => x.RoleId)
                    .ToListAsync();

                var roles = await dbContext.Roles
                    .Where(r => roleIds.Contains(r.Id))
                    .ToListAsync();
                // 生成新的JWT令牌
                var newToken = GenerateJwtToken(user, roles);
                var newRefreshToken = GenerateRefreshToken(user);

                // 设置到cookie
                httpContextAccessor.HttpContext?.Response.Cookies.Append("refreshToken", newRefreshToken,
                    CreateCookieOptions(jwtOptions.RefreshExpireMinutes));
                httpContextAccessor.HttpContext?.Response.Cookies.Append("token", newToken,
                    CreateCookieOptions(jwtOptions.ExpireMinutes));

                return (true, newToken, newRefreshToken, null);
            }
            catch (SecurityTokenException)
            {
                return (false, string.Empty, null, "无效的刷新令牌");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "刷新令牌失败");
            return (false, string.Empty, null, "刷新令牌失败，请重新登录");
        }
    }

    /// <summary>
    /// 生成JWT令牌
    /// </summary>
    /// <param name="user">用户</param>
    /// <param name="roles"></param>
    /// <returns>JWT令牌</returns>
    private string GenerateJwtToken(User user, List<Role> roles)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, string.Join(',', roles.Select(x => x.Name)))
        };

        var key = jwtOptions.GetSymmetricSecurityKey();
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.Now.AddMinutes(jwtOptions.ExpireMinutes);

        var token = new JwtSecurityToken(
            issuer: jwtOptions.Issuer,
            audience: jwtOptions.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// 生成刷新令牌
    /// </summary>
    /// <param name="user">用户</param>
    /// <returns>刷新令牌</returns>
    private string GenerateRefreshToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id)
        };

        var key = jwtOptions.GetSymmetricSecurityKey();
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.Now.AddMinutes(jwtOptions.RefreshExpireMinutes);

        var token = new JwtSecurityToken(
            issuer: jwtOptions.Issuer,
            audience: jwtOptions.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// 验证Google ID令牌
    /// </summary>
    /// <param name="idToken">ID令牌</param>
    /// <returns>验证结果</returns>
    private async Task<GooglePayload?> ValidateGoogleIdToken(string idToken)
    {
        try
        {
            var clientId = configuration["Google:ClientId"];
            if (string.IsNullOrEmpty(clientId))
            {
                return null;
            }

            // 这里简化了Google ID令牌验证过程
            // 实际应用中应使用Google API客户端库进行验证
            // 或者调用Google OAuth2 TokenInfo端点

            // 模拟验证成功，返回解析的payload
            // 实际应用中需要实现完整的验证逻辑
            var tokenParts = idToken.Split('.');
            if (tokenParts.Length != 3)
            {
                return null;
            }

            var payloadJson = Encoding.UTF8.GetString(
                Convert.FromBase64String(tokenParts[1].PadRight(4 * ((tokenParts[1].Length + 3) / 4), '=')
                    .Replace('-', '+').Replace('_', '/')));

            var payload = JsonSerializer.Deserialize<GooglePayload>(payloadJson);
            return payload;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取支持的第三方登录方式
    /// </summary>
    public async Task<List<SupportedThirdPartyLoginsDto>> GetSupportedThirdPartyLoginsAsync()
    {
        var supportedLogins = new List<SupportedThirdPartyLoginsDto>();

        // 检查GitHub配置
        if (!string.IsNullOrEmpty(configuration["GitHub:ClientId"]) &&
            !string.IsNullOrEmpty(configuration["GitHub:ClientSecret"]))
        {
            supportedLogins.Add(new SupportedThirdPartyLoginsDto
            {
                Name = "GitHub",
                Icon = "https://github.githubassets.com/images/modules/logos_page/GitHub-Mark.png",
                ClientId = configuration["GitHub:ClientId"] ?? string.Empty,
                RedirectUri = configuration["GitHub:RedirectUri"] ?? string.Empty
            });
        }

        // 检查Google配置
        if (!string.IsNullOrEmpty(configuration["Google:ClientId"]) &&
            !string.IsNullOrEmpty(configuration["Google:ClientSecret"]))
        {
            supportedLogins.Add(new SupportedThirdPartyLoginsDto
            {
                Name = "Google",
                Icon = "https://www.google.com/favicon.ico",
                ClientId = configuration["Google:ClientId"] ?? string.Empty,
                RedirectUri = configuration["Google:RedirectUri"] ?? string.Empty
            });
        }

        return await Task.FromResult(supportedLogins);
    }

    /// <summary>
    /// 测试方法：检查当前请求中的cookie和认证状态
    /// </summary>
    /// <returns>调试信息</returns>
    public async Task<object> GetAuthDebugInfoAsync()
    {
        var context = httpContextAccessor.HttpContext;
        if (context == null)
        {
            return new { message = "HttpContext为空" };
        }

        var tokenFromCookie = context.Request.Cookies["token"];
        var refreshTokenFromCookie = context.Request.Cookies["refreshToken"];
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        var isAuthenticated = context.User?.Identity?.IsAuthenticated ?? false;
        var userName = context.User?.Identity?.Name;
        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return await Task.FromResult(new
        {
            HasTokenCookie = !string.IsNullOrEmpty(tokenFromCookie),
            TokenCookieLength = tokenFromCookie?.Length ?? 0,
            HasRefreshTokenCookie = !string.IsNullOrEmpty(refreshTokenFromCookie),
            RefreshTokenCookieLength = refreshTokenFromCookie?.Length ?? 0,
            HasAuthorizationHeader = !string.IsNullOrEmpty(authHeader),
            AuthorizationHeader = authHeader?.Substring(0, Math.Min(20, authHeader?.Length ?? 0)) + "...",
            IsAuthenticated = isAuthenticated,
            UserName = userName,
            UserId = userId,
            RequestUrl = context.Request.Path,
            RequestMethod = context.Request.Method,
            IsHttps = context.Request.IsHttps,
            Environment = configuration.GetValue<string>("ASPNETCORE_ENVIRONMENT"),
            AllCookies = context.Request.Cookies.Keys.ToArray()
        });
    }

    /// <summary>
    /// 创建cookie选项，根据环境调整安全设置
    /// </summary>
    /// <param name="expireMinutes">过期时间（分钟）</param>
    /// <returns>cookie选项</returns>
    private CookieOptions CreateCookieOptions(int expireMinutes)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = false, // 开发环境或HTTP时不要求HTTPS
            SameSite = SameSiteMode.Lax, // 开发环境使用更宽松的策略
            Expires = DateTime.UtcNow.AddMinutes(expireMinutes)
        };
    }
}

public class SupportedThirdPartyLoginsDto
{
    public string Name { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;
}

/// <summary>
/// Google Payload
/// </summary>
public class GooglePayload
{
    /// <summary>
    /// 邮箱
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 是否验证邮箱
    /// </summary>
    public bool EmailVerified { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 头像
    /// </summary>
    public string Picture { get; set; } = string.Empty;
}