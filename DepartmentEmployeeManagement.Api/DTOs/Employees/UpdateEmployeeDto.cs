using System.ComponentModel.DataAnnotations;

namespace DepartmentEmployeeManagement.Api.DTOs.Employees;

public class UpdateEmployeeDto
{
    [Required, StringLength(80, MinimumLength = 1)] public string FirstName { get; set; } = string.Empty;
    [Required, StringLength(80, MinimumLength = 1)] public string LastName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
    [Phone, StringLength(30)] public string? PhoneNumber { get; set; }
    [Required] public DateTime DateOfBirth { get; set; }
    [Range(1, int.MaxValue)] public int DepartmentId { get; set; }
    [StringLength(500)] public string? ImageUrl { get; set; }
}
