using Microsoft.AspNetCore.Identity;

namespace DepartmentEmployeeManagement.Api.Identity;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
}
