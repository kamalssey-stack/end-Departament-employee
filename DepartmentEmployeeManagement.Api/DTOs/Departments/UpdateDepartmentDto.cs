using System.ComponentModel.DataAnnotations;

namespace DepartmentEmployeeManagement.Api.DTOs.Departments;

public class UpdateDepartmentDto
{
    [Required, StringLength(120, MinimumLength = 2)] public string Name { get; set; } = string.Empty;
    [StringLength(1000)] public string? Description { get; set; }
    [StringLength(200)] public string? Location { get; set; }
}
