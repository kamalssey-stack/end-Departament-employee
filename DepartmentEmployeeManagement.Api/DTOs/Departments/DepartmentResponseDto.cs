namespace DepartmentEmployeeManagement.Api.DTOs.Departments;

public class DepartmentResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
    public string? ImageUrl { get; set; }
    public int EmployeeCount { get; set; }
    public DateTime? CreatedAt { get; set; }
}
