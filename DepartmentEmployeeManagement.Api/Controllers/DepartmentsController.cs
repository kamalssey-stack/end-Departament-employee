using AutoMapper;
using DepartmentEmployeeManagement.Api.Data;
using DepartmentEmployeeManagement.Api.DTOs.Common;
using DepartmentEmployeeManagement.Api.DTOs.Departments;
using DepartmentEmployeeManagement.Api.Entities;
using DepartmentEmployeeManagement.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DepartmentEmployeeManagement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class DepartmentsController(AppDbContext db, IMapper mapper, IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<DepartmentResponseDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DepartmentResponseDto>> Create(CreateDepartmentDto dto)
    {
        var entity = mapper.Map<Department>(dto);
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, mapper.Map<DepartmentResponseDto>(entity));
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<DepartmentResponseDto>>> GetAll([FromQuery] string? name, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Departments.AsNoTracking().Include(d => d.Employees).AsQueryable();
        if (!string.IsNullOrWhiteSpace(name)) query = query.Where(d => d.Name.Contains(name));
        var total = await query.CountAsync();
        var rows = await query.OrderBy(d => d.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new PagedResponse<DepartmentResponseDto>(mapper.Map<List<DepartmentResponseDto>>(rows), page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<DepartmentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentDetailDto>> GetById(int id)
    {
        var entity = await db.Departments.Include(d => d.Employees).FirstOrDefaultAsync(d => d.Id == id);
        return entity is null ? NotFound() : Ok(mapper.Map<DepartmentDetailDto>(entity));
    }

    [HttpGet("{id:int}/statistics")]
    [ProducesResponseType<DepartmentStatisticsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentStatisticsDto>> GetStatistics(int id)
    {
        var department = await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (department is null) return NotFound();

        var employeeQuery = db.Employees.AsNoTracking().Where(e => e.DepartmentId == id);
        var employeeCount = await employeeQuery.CountAsync();
        var latest = await employeeQuery
            .Include(e => e.Department)
            .Where(e => e.CreatedAt != null)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .FirstOrDefaultAsync();
        var birthDates = await employeeQuery.Select(e => e.DateOfBirth).ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ages = birthDates.Select(dateOfBirth =>
        {
            var birthDate = DateOnly.FromDateTime(dateOfBirth);
            var age = today.Year - birthDate.Year;
            if (birthDate > today.AddYears(-age)) age--;
            return age;
        }).ToArray();

        return Ok(new DepartmentStatisticsDto
        {
            DepartmentId = department.Id,
            DepartmentName = department.Name,
            DepartmentCreatedAt = department.CreatedAt.HasValue ? DateTime.SpecifyKind(department.CreatedAt.Value, DateTimeKind.Utc) : null,
            EmployeeCount = employeeCount,
            LatestEmployee = latest is null ? null : mapper.Map<DepartmentEmployeeManagement.Api.DTOs.Employees.EmployeeResponseDto>(latest),
            AverageEmployeeAge = ages.Length == 0 ? null : Math.Round(ages.Average(), 1)
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateDepartmentDto dto)
    {
        var entity = await db.Departments.FindAsync(id);
        if (entity is null) return NotFound();
        mapper.Map(dto, entity);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.Departments.FindAsync(id);
        if (entity is null) return NotFound();
        if (await db.Employees.AnyAsync(e => e.DepartmentId == id)) return Conflict(new { message = "Employees must be reassigned or deleted before deleting this department." });
        db.Departments.Remove(entity);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/image")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        var department = await db.Departments.FindAsync(id);
        if (department is null) return NotFound();
        if (file is null || file.Length == 0) return BadRequest(new { message = "Select an image file." });
        if (!ImageUploadValidator.TryGetStoredExtension(file, out var ext))
            return BadRequest(new { message = "Choose a valid JPG, PNG, GIF, WEBP, BMP, or AVIF photo up to 20 MB." });
        var uploads = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads");
        Directory.CreateDirectory(uploads);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        await using (var stream = System.IO.File.Create(Path.Combine(uploads, fileName))) await file.CopyToAsync(stream);
        department.ImageUrl = $"/uploads/{fileName}";
        await db.SaveChangesAsync();
        return Ok(new { imageUrl = department.ImageUrl });
    }
}
