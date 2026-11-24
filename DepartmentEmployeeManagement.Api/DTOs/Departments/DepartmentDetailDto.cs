using DepartmentEmployeeManagement.Api.DTOs.Employees;

namespace DepartmentEmployeeManagement.Api.DTOs.Departments;

public class DepartmentDetailDto : DepartmentResponseDto
{
    public IReadOnlyList<EmployeeResponseDto> Employees { get; set; } = [];
}
