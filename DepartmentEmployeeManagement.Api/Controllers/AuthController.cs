using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using DepartmentEmployeeManagement.Api.DTOs.Auth;
using DepartmentEmployeeManagement.Api.Data;
using DepartmentEmployeeManagement.Api.Entities;
using DepartmentEmployeeManagement.Api.Identity;
using DepartmentEmployeeManagement.Api.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace DepartmentEmployeeManagement.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<AppUser> users,
    AppDbContext db,
    RoleManager<IdentityRole> roles,
    SignInManager<AppUser> signIn,
    IAntiforgery antiforgery,
    IAppEmailSender emailSender,
    IConfiguration configuration,
    IWebHostEnvironment environment) : ControllerBase
{
    private const string RefreshCookieName = ".DepartmentEmployeeManagement.Refresh";

    [HttpGet("csrf")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public IActionResult GetCsrfToken() => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        var email = dto.Email.Trim();
        var user = new AppUser { UserName = email, Email = email, DisplayName = dto.DisplayName.Trim() };
        var result = await users.CreateAsync(user, dto.Password);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        if (!await roles.RoleExistsAsync("User")) await roles.CreateAsync(new IdentityRole("User"));
        if (!await roles.RoleExistsAsync("Admin")) await roles.CreateAsync(new IdentityRole("Admin"));
        var assignedRole = users.Users.Count() <= 1 ? "Admin" : "User";
        await users.AddToRoleAsync(user, assignedRole);

        var link = await SendConfirmationLinkAsync(user);
        return Ok(new
        {
            message = "Аккаунт создан. Проверьте письмо и подтвердите email перед входом.",
            previewUrl = GetDevelopmentPreview(link),
            previewSubject = "Подтвердите email"
        });
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailDto dto)
    {
        var user = await users.FindByEmailAsync(dto.Email.Trim());
        if (user is null) return BadRequest(new { message = "Ссылка недействительна или устарела." });
        try
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(dto.Token));
            var result = await users.ConfirmEmailAsync(user, token);
            if (result.Succeeded) return Ok(new { message = "Email подтверждён. Теперь можно войти." });
            return BadRequest(new { message = "Ссылка недействительна или устарела." });
        }
        catch (FormatException)
        {
            return BadRequest(new { message = "Ссылка недействительна или устарела." });
        }
    }

    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationDto dto)
    {
        var user = await users.FindByEmailAsync(dto.Email.Trim());
        string? link = null;
        if (user is not null && !user.EmailConfirmed) link = await SendConfirmationLinkAsync(user);
        return Ok(new
        {
            message = "Если аккаунт существует и email ещё не подтверждён, письмо отправлено.",
            previewUrl = link is null ? null : GetDevelopmentPreview(link),
            previewSubject = "Подтвердите email"
        });
    }

    private async Task<string> SendConfirmationLinkAsync(AppUser user)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = $"{Request.Scheme}://{Request.Host}/?email={Uri.EscapeDataString(user.Email!)}&confirmToken={Uri.EscapeDataString(encodedToken)}#confirm";
        await emailSender.SendEmailConfirmationAsync(user.Email!, link, HttpContext.RequestAborted);
        return link;
    }

    private string? GetDevelopmentPreview(string link) =>
        environment.IsDevelopment() && string.IsNullOrWhiteSpace(configuration["Smtp:Host"] ?? configuration["Email:Host"]) ? link : null;

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponseDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TokenResponseDto>> Login(LoginDto dto)
    {
        var user = await users.FindByEmailAsync(dto.Email.Trim());
        if (user is null) return Unauthorized(new { message = "Неверный email или пароль." });
        var result = await signIn.PasswordSignInAsync(user, dto.Password, dto.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            var accessExpiresAtUtc = DateTime.UtcNow.AddMinutes(15);
            var token = await GenerateJwtTokenAsync(user, accessExpiresAtUtc);
            var refreshToken = CreateRefreshToken(user, DateTime.UtcNow);
            db.RefreshTokens.Add(refreshToken.Entity);
            await db.SaveChangesAsync();
            SetRefreshCookie(refreshToken.PlainText, refreshToken.Entity.ExpiresAtUtc);
            var userRoles = await users.GetRolesAsync(user);
            return Ok(new TokenResponseDto
            {
                Token = token,
                RefreshToken = refreshToken.PlainText,
                AccessExpiresAtUtc = accessExpiresAtUtc,
                RefreshTokenExpiresAtUtc = refreshToken.Entity.ExpiresAtUtc,
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Roles = userRoles.ToArray()
            });
        }
        if (result.IsNotAllowed && !user.EmailConfirmed)
            return Unauthorized(new { message = "Сначала подтвердите email. При необходимости запросите письмо ещё раз." });
        if (result.IsLockedOut) return StatusCode(StatusCodes.Status423Locked, new { message = "Слишком много попыток. Попробуйте позже." });
        return Unauthorized(new { message = "Неверный email или пароль." });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<ActionResult<TokenResponseDto>> Refresh(RefreshTokenRequestDto dto) => RotateRefreshTokenAsync(dto.RefreshToken);

    [HttpPost("refresh-cookie")]
    [AllowAnonymous]
    [ProducesResponseType<TokenResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<ActionResult<TokenResponseDto>> RefreshFromCookie()
    {
        var refreshToken = Request.Cookies[RefreshCookieName];
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Task.FromResult<ActionResult<TokenResponseDto>>(Unauthorized(new { message = "Refresh session is missing. Sign in again." }));
        return RotateRefreshTokenAsync(refreshToken);
    }

    private async Task<ActionResult<TokenResponseDto>> RotateRefreshTokenAsync(string plainTextRefreshToken)
    {
        var now = DateTime.UtcNow;
        var tokenHash = HashRefreshToken(plainTextRefreshToken);
        var storedToken = await db.RefreshTokens.Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash);
        if (storedToken is null) return Unauthorized(new { message = "Refresh token is invalid or expired. Sign in again." });

        if (storedToken.RevokedAtUtc.HasValue)
        {
            await db.RefreshTokens.Where(token => token.UserId == storedToken.UserId && token.RevokedAtUtc == null)
                .ExecuteUpdateAsync(update => update.SetProperty(token => token.RevokedAtUtc, now));
            ClearRefreshCookie();
            return Unauthorized(new { message = "Refresh token was already used. All sessions were revoked; sign in again." });
        }

        if (storedToken.ExpiresAtUtc <= now || !storedToken.User.EmailConfirmed)
        {
            storedToken.RevokedAtUtc = now;
            await db.SaveChangesAsync();
            ClearRefreshCookie();
            return Unauthorized(new { message = "Refresh token is invalid or expired. Sign in again." });
        }

        var rotated = CreateRefreshToken(storedToken.User, now);
        storedToken.RevokedAtUtc = now;
        storedToken.ReplacedByTokenHash = rotated.Entity.TokenHash;
        db.RefreshTokens.Add(rotated.Entity);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            ClearRefreshCookie();
            return Unauthorized(new { message = "Refresh token was already used. Sign in again." });
        }
        SetRefreshCookie(rotated.PlainText, rotated.Entity.ExpiresAtUtc);

        var accessExpiresAtUtc = now.AddMinutes(15);
        var accessToken = await GenerateJwtTokenAsync(storedToken.User, accessExpiresAtUtc);
        var userRoles = await users.GetRolesAsync(storedToken.User);
        return Ok(new TokenResponseDto
        {
            Token = accessToken,
            RefreshToken = rotated.PlainText,
            AccessExpiresAtUtc = accessExpiresAtUtc,
            RefreshTokenExpiresAtUtc = rotated.Entity.ExpiresAtUtc,
            Id = storedToken.User.Id,
            Email = storedToken.User.Email,
            DisplayName = storedToken.User.DisplayName,
            Roles = userRoles.ToArray()
        });
    }

    private void SetRefreshCookie(string token, DateTime expiresAtUtc) => Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)),
        IsEssential = true
    });

    private void ClearRefreshCookie() => Response.Cookies.Delete(RefreshCookieName, new CookieOptions
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth"
    });

    private static (RefreshToken Entity, string PlainText) CreateRefreshToken(AppUser user, DateTime now)
    {
        var plainText = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashRefreshToken(plainText),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(30)
        };
        return (entity, plainText);
    }

    private static string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task<string> GenerateJwtTokenAsync(AppUser user, DateTime expiresAtUtc)
    {
        var key = configuration["Jwt:Key"] ?? "DepartmentEmployeeManagementSecretKeyForJwtSigning123!";
        var issuer = configuration["Jwt:Issuer"] ?? "DepartmentEmployeeManagement";
        var audience = configuration["Jwt:Audience"] ?? "DepartmentEmployeeManagement";

        var userRoles = await users.GetRolesAsync(user);
        if (!userRoles.Any())
        {
            if (!await roles.RoleExistsAsync("User")) await roles.CreateAsync(new IdentityRole("User"));
            await users.AddToRoleAsync(user, "User");
            userRoles = new List<string> { "User" };
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? ""),
            new(ClaimTypes.Email, user.Email ?? ""),
            new("displayName", user.DisplayName ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var role in userRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var now = DateTime.UtcNow;
            await db.RefreshTokens.Where(token => token.UserId == userId && token.RevokedAtUtc == null)
                .ExecuteUpdateAsync(update => update.SetProperty(token => token.RevokedAtUtc, now));
        }
        ClearRefreshCookie();
        await signIn.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (userId is not null)
            {
                user = await users.FindByIdAsync(userId);
            }
        }
        return user is null ? Unauthorized() : Ok(new { id = user.Id, email = user.Email, displayName = user.DisplayName });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto)
    {
        var user = await users.FindByEmailAsync(dto.Email.Trim());
        if (user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = $"{Request.Scheme}://{Request.Host}/?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(encodedToken)}#reset";
            await emailSender.SendPasswordResetAsync(user.Email!, link, HttpContext.RequestAborted);
            return Ok(new
            {
                message = "Если такой аккаунт зарегистрирован, инструкция отправлена на его email.",
                previewUrl = GetDevelopmentPreview(link),
                previewSubject = "Сброс пароля"
            });
        }
        return Ok(new { message = "Если такой аккаунт зарегистрирован, инструкция отправлена на его email." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
    {
        try
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(dto.Token));
            var user = await users.FindByEmailAsync(dto.Email.Trim());
            if (user is null) return BadRequest(new { message = "Ссылка недействительна или устарела." });
            var result = await users.ResetPasswordAsync(user, token, dto.NewPassword);
            if (result.Succeeded)
            {
                await db.RefreshTokens.Where(refresh => refresh.UserId == user.Id && refresh.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(refresh => refresh.RevokedAtUtc, DateTime.UtcNow));
                return Ok(new { message = "Пароль изменён. Теперь можно войти." });
            }
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }
        catch (FormatException)
        {
            return BadRequest(new { message = "Ссылка недействительна или устарела." });
        }
    }
}
