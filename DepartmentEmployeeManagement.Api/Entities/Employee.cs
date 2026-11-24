namespace DepartmentEmployeeManagement.Api.Entities;

public class Employee
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DateTime DateOfBirth { get; set; }
    public int DepartmentId { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
    public Department Department { get; set; } = null!;
}
