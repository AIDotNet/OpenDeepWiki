using KoalaWiki.Services;
using Xunit;

namespace KoalaWiki.Tests.Services;

/// <summary>
/// 密码服务测试
/// </summary>
public class PasswordServiceTests
{
    private readonly IPasswordService _passwordService;

    public PasswordServiceTests()
    {
        _passwordService = new PasswordService();
    }

    [Fact]
    public void HashPassword_ValidPassword_ReturnsHashedPassword()
    {
        // Arrange
        var password = "testPassword123";

        // Act
        var hashedPassword = _passwordService.HashPassword(password);

        // Assert
        Assert.NotNull(hashedPassword);
        Assert.NotEqual(password, hashedPassword);
        Assert.True(hashedPassword.StartsWith("$2a$") || 
                   hashedPassword.StartsWith("$2b$") ||
                   hashedPassword.StartsWith("$2x$") ||
                   hashedPassword.StartsWith("$2y$"));
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        // Arrange
        var password = "testPassword123";
        var hashedPassword = _passwordService.HashPassword(password);

        // Act
        var result = _passwordService.VerifyPassword(password, hashedPassword);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_IncorrectPassword_ReturnsFalse()
    {
        // Arrange
        var password = "testPassword123";
        var wrongPassword = "wrongPassword456";
        var hashedPassword = _passwordService.HashPassword(password);

        // Act
        var result = _passwordService.VerifyPassword(wrongPassword, hashedPassword);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsPlainTextPassword_PlainTextPassword_ReturnsTrue()
    {
        // Arrange
        var plainTextPassword = "admin";

        // Act
        var result = _passwordService.IsPlainTextPassword(plainTextPassword);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsPlainTextPassword_HashedPassword_ReturnsFalse()
    {
        // Arrange
        var hashedPassword = "$2a$12$hashedPasswordExample";

        // Act
        var result = _passwordService.IsPlainTextPassword(hashedPassword);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void HashPassword_NullOrEmptyPassword_ThrowsArgumentException(string password)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _passwordService.HashPassword(password));
    }

    [Theory]
    [InlineData("", "hashedPassword")]
    [InlineData(null, "hashedPassword")]
    [InlineData("password", "")]
    [InlineData("password", null)]
    public void VerifyPassword_NullOrEmptyInputs_ReturnsFalse(string password, string hashedPassword)
    {
        // Act
        var result = _passwordService.VerifyPassword(password, hashedPassword);

        // Assert
        Assert.False(result);
    }
}