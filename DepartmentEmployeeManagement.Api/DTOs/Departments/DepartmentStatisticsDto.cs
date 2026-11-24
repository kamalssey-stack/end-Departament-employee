using DepartmentEmployeeManagement.Api.DTOs.Employees;

namespace DepartmentEmployeeManagement.Api.DTOs.Departments;

public sealed class DepartmentStatisticsDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public DateTime? DepartmentCreatedAt { get; set; }
    public int EmployeeCount { get; set; }
    public EmployeeResponseDto? LatestEmployee { get; set; }
    public double? AverageEmployeeAge { get; set; }
}
