using AutoMapper;
using DepartmentEmployeeManagement.Api.DTOs.Departments;
using DepartmentEmployeeManagement.Api.DTOs.Employees;
using DepartmentEmployeeManagement.Api.Entities;

namespace DepartmentEmployeeManagement.Api.Mapping;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<CreateDepartmentDto, Department>();
        CreateMap<UpdateDepartmentDto, Department>();
        CreateMap<Department, DepartmentResponseDto>()
            .ForMember(d => d.EmployeeCount, o => o.MapFrom(s => s.Employees.Count))
            .ForMember(d => d.CreatedAt, o => o.MapFrom((Department s) => s.CreatedAt.HasValue ? DateTime.SpecifyKind(s.CreatedAt.Value, DateTimeKind.Utc) : (DateTime?)null));
        CreateMap<Department, DepartmentDetailDto>()
            .ForMember(d => d.EmployeeCount, o => o.MapFrom(s => s.Employees.Count))
            .ForMember(d => d.CreatedAt, o => o.MapFrom((Department s) => s.CreatedAt.HasValue ? DateTime.SpecifyKind(s.CreatedAt.Value, DateTimeKind.Utc) : (DateTime?)null));
        CreateMap<CreateEmployeeDto, Employee>();
        CreateMap<UpdateEmployeeDto, Employee>();
        CreateMap<Employee, EmployeeResponseDto>()
            .ForMember(d => d.DepartmentName, o => o.MapFrom(s => s.Department.Name))
            .ForMember(d => d.CreatedAt, o => o.MapFrom((Employee s) => s.CreatedAt.HasValue ? DateTime.SpecifyKind(s.CreatedAt.Value, DateTimeKind.Utc) : (DateTime?)null));
        CreateMap<Employee, EmployeeDetailDto>()
            .ForMember(d => d.DepartmentName, o => o.MapFrom(s => s.Department.Name))
            .ForMember(d => d.CreatedAt, o => o.MapFrom((Employee s) => s.CreatedAt.HasValue ? DateTime.SpecifyKind(s.CreatedAt.Value, DateTimeKind.Utc) : (DateTime?)null));
    }
}
