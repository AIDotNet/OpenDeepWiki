using Microsoft.AspNetCore.Mvc;
using OpenDeepWiki.Models.Auth;
using OpenDeepWiki.Services.Auth;

namespace OpenDeepWiki.Endpoints;

/// <summary>
/// 认证相关端点
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("认证");

        // 用户登录
        group.MapPost("/login", async ([FromBody] LoginRequest request, [FromServices] IAuthService authService) =>
        {
            try
            {
                var response = await authService.LoginAsync(request);
                return Results.Ok(new { success = true, data = response });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Unauthorized();
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        })
        .AllowAnonymous()
        .WithName("Login")
        .WithSummary("用户登录")
        .Produces<LoginResponse>(200)
        .Produces(401)
        .Produces(400);

        // 用户注册
        group.MapPost("/register", async ([FromBody] RegisterRequest request, [FromServices] IAuthService authService) =>
        {
            try
            {
                var response = await authService.RegisterAsync(request);
                return Results.Ok(new { success = true, data = response });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        })
        .AllowAnonymous()
        .WithName("Register")
        .WithSummary("用户注册")
        .Produces<LoginResponse>(200)
        .Produces(400);

        // 获取当前用户信息
        group.MapGet("/me", async (HttpContext context, [FromServices] IAuthService authService) =>
        {
            var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var userInfo = await authService.GetUserInfoAsync(userId);

            if (userInfo == null)
            {
                return Results.NotFound(new { success = false, message = "用户不存在" });
            }

            return Results.Ok(new { success = true, data = userInfo });
        })
        .RequireAuthorization()
        .WithName("GetCurrentUser")
        .WithSummary("获取当前用户信息")
        .Produces<UserInfo>(200)
        .Produces(401)
        .Produces(404);

        return app;
    }
}
