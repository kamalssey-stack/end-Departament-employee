using DepartmentEmployeeManagement.Api.Identity;

namespace DepartmentEmployeeManagement.Api.Entities;

public sealed class RefreshToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public string? ReplacedByTokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
