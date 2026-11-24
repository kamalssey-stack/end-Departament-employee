namespace DepartmentEmployeeManagement.Api.DTOs.Employees;

public sealed class EmployeeImportResultDto
{
    public int SuccessfulCount { get; set; }
    public int FailedCount { get; set; }
    public IReadOnlyList<EmployeeImportRowErrorDto> Errors { get; set; } = [];
}

public sealed class EmployeeImportRowErrorDto
{
    public int RowNumber { get; set; }
    public string? Email { get; set; }
    public IReadOnlyList<string> Errors { get; set; } = [];
}
