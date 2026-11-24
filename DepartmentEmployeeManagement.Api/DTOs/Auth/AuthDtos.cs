using System.ComponentModel.DataAnnotations;

namespace DepartmentEmployeeManagement.Api.DTOs.Auth;

public sealed class RegisterDto
{
    [Required, StringLength(100, MinimumLength = 2)] public string DisplayName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 8)] public string Password { get; set; } = string.Empty;
}

public sealed class LoginDto
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}

public sealed class RefreshTokenRequestDto
{
    [Required, StringLength(200)] public string RefreshToken { get; set; } = string.Empty;
}

public sealed class TokenResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessExpiresAtUtc { get; set; }
    public DateTime RefreshTokenExpiresAtUtc { get; set; }
    public string Id { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
}

public sealed class ForgotPasswordDto
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
}

public sealed class ConfirmEmailDto
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Token { get; set; } = string.Empty;
}

public sealed class ResendConfirmationDto
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordDto
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Token { get; set; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 8)] public string NewPassword { get; set; } = string.Empty;
}
