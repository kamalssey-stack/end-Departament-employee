using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AutoMapper;
using DepartmentEmployeeManagement.Api.Data;
using DepartmentEmployeeManagement.Api.DTOs.Common;
using DepartmentEmployeeManagement.Api.DTOs.Employees;
using DepartmentEmployeeManagement.Api.Entities;
using DepartmentEmployeeManagement.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace DepartmentEmployeeManagement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class EmployeesController(AppDbContext db, IMapper mapper, IWebHostEnvironment environment, ExcelWorkbookService excel) : ControllerBase
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly string[] ImportHeaders = ["FirstName", "LastName", "Email", "PhoneNumber", "DateOfBirth", "DepartmentId"];

    [HttpGet("export")]
    [Produces(ExcelContentType)]
    public async Task<IActionResult> Export([FromQuery] int? departmentId, [FromQuery] string? search)
    {
        var query = db.Employees.AsNoTracking().Include(e => e.Department).AsQueryable();
        if (departmentId.HasValue) query = query.Where(e => e.DepartmentId == departmentId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(e => e.FirstName.Contains(term) || e.LastName.Contains(term) || e.Email.Contains(term));
        }

        var employees = await query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync();
        var rows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Id", "FirstName", "LastName", "Email", "PhoneNumber", "DateOfBirth", "DepartmentId", "DepartmentName", "CreatedAt" }
        };
        rows.AddRange(employees.Select(e => (IReadOnlyList<string?>)new string?[] {
            e.Id.ToString(CultureInfo.InvariantCulture), e.FirstName, e.LastName, e.Email, e.PhoneNumber,
            e.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), e.DepartmentId.ToString(CultureInfo.InvariantCulture),
            e.Department.Name, e.CreatedAt.HasValue ? DateTime.SpecifyKind(e.CreatedAt.Value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture) : null }));

        var bytes = excel.CreateWorkbook(rows);
        return File(bytes, ExcelContentType, $"employees-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
    }

    [HttpGet("import-template")]
    [Produces(ExcelContentType)]
    public IActionResult ImportTemplate()
    {
        var rows = new IReadOnlyList<string?>[] { ImportHeaders };
        return File(excel.CreateWorkbook(rows), ExcelContentType, "employee-import-template.xlsx");
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<EmployeeImportResultDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EmployeeImportResultDto>> Import(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Select an .xlsx file." });
        if (file.Length > 10 * 1024 * 1024) return BadRequest(new { message = "Excel file must be 10 MB or smaller." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only .xlsx Excel workbooks are supported." });

        ExcelWorksheetData worksheet;
        try
        {
            await using var stream = file.OpenReadStream();
            worksheet = excel.ReadFirstSheet(stream);
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (System.Xml.XmlException)
        {
            return BadRequest(new { message = "The first worksheet is invalid." });
        }
        var missingHeaders = ImportHeaders.Except(worksheet.Headers, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missingHeaders.Length > 0)
            return BadRequest(new { message = "The worksheet is missing required columns.", missingHeaders });

        var rows = worksheet.Rows;

        if (rows.Count == 0) return BadRequest(new { message = "The worksheet contains no employee rows." });
        if (rows.Count > 5000) return BadRequest(new { message = "Import is limited to 5,000 rows per file." });

        var prepared = new List<(int RowNumber, CreateEmployeeDto Dto, List<string> Errors)>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var values = row.Values;
            var dto = new CreateEmployeeDto
            {
                FirstName = Read(values, "FirstName"),
                LastName = Read(values, "LastName"),
                Email = Read(values, "Email").Trim(),
                PhoneNumber = NullIfEmpty(Read(values, "PhoneNumber")),
                DepartmentId = int.TryParse(Read(values, "DepartmentId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0
            };

            var errors = new List<string>();
            if (!TryParseExcelDate(Read(values, "DateOfBirth"), out var dateOfBirth))
                errors.Add("DateOfBirth must be a valid date (recommended format: yyyy-MM-dd)." );
            else
            {
                dto.DateOfBirth = dateOfBirth;
                if (dateOfBirth.Date > DateTime.UtcNow.Date) errors.Add("DateOfBirth cannot be in the future.");
            }

            var validationResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, validateAllProperties: true))
                errors.AddRange(validationResults.Select(result => result.ErrorMessage ?? "Invalid value."));
            if (dto.DepartmentId <= 0) errors.Add("DepartmentId must be a positive number.");
            if (dto.Email.Length > 0 && !seenEmails.Add(dto.Email)) errors.Add("Email is duplicated in this file.");
            prepared.Add((row.RowNumber, dto, errors.Distinct().ToList()));
        }

        var candidateEmails = prepared.Where(row => row.Dto.Email.Length > 0).Select(row => row.Dto.Email).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existingEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in candidateEmails.Chunk(1000))
        {
            var normalizedBatch = batch.Select(email => email.ToLower()).ToArray();
            var found = await db.Employees.AsNoTracking()
                .Where(e => normalizedBatch.Contains(e.Email.ToLower()))
                .Select(e => e.Email)
                .ToListAsync(cancellationToken);
            existingEmails.UnionWith(found);
        }

        var candidateDepartmentIds = prepared.Where(row => row.Dto.DepartmentId > 0).Select(row => row.Dto.DepartmentId).Distinct().ToArray();
        var existingDepartmentIds = new HashSet<int>();
        foreach (var batch in candidateDepartmentIds.Chunk(1000))
        {
            var found = await db.Departments.AsNoTracking().Where(d => batch.Contains(d.Id)).Select(d => d.Id).ToListAsync(cancellationToken);
            existingDepartmentIds.UnionWith(found);
        }

        var result = new EmployeeImportResultDto();
        foreach (var row in prepared)
        {
            var errors = row.Errors;
            if (existingEmails.Contains(row.Dto.Email)) errors.Add("An employee with this email already exists.");
            if (row.Dto.DepartmentId > 0 && !existingDepartmentIds.Contains(row.Dto.DepartmentId)) errors.Add("DepartmentId does not exist.");
            if (errors.Count > 0)
            {
                result.Errors = result.Errors.Append(new EmployeeImportRowErrorDto { RowNumber = row.RowNumber, Email = row.Dto.Email, Errors = errors }).ToArray();
                result.FailedCount++;
                continue;
            }

            var entity = mapper.Map<Employee>(row.Dto);
            db.Employees.Add(entity);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                existingEmails.Add(entity.Email);
                result.SuccessfulCount++;
            }
            catch (DbUpdateException)
            {
                db.Entry(entity).State = EntityState.Detached;
                result.Errors = result.Errors.Append(new EmployeeImportRowErrorDto
                {
                    RowNumber = row.RowNumber,
                    Email = row.Dto.Email,
                    Errors = ["Could not save this row. Check for a duplicate email and valid department."]
                }).ToArray();
                result.FailedCount++;
            }
        }

        return Ok(result);
    }

    private static string Read(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? value.Trim() : string.Empty;
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool TryParseExcelDate(string value, out DateTime date)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) return true;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is >= 1 and <= 2958465)
        {
            try { date = DateTime.FromOADate(serial); return true; }
            catch (ArgumentException) { }
        }
        date = default;
        return false;
    }

    [HttpPost]
    public async Task<ActionResult<EmployeeResponseDto>> Create(CreateEmployeeDto dto)
    {
        if (!await db.Departments.AnyAsync(d => d.Id == dto.DepartmentId)) return BadRequest(new { message = "DepartmentId does not exist." });
        var entity = mapper.Map<Employee>(dto);
        db.Employees.Add(entity);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { message = "An employee with this email already exists." }); }
        await db.Entry(entity).Reference(e => e.Department).LoadAsync();
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, mapper.Map<EmployeeResponseDto>(entity));
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<EmployeeResponseDto>>> GetAll([FromQuery] int? departmentId, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Employees.AsNoTracking().Include(e => e.Department).AsQueryable();
        if (departmentId.HasValue) query = query.Where(e => e.DepartmentId == departmentId.Value);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(e => e.FirstName.Contains(search) || e.LastName.Contains(search) || e.Email.Contains(search));
        var total = await query.CountAsync();
        var rows = await query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new PagedResponse<EmployeeResponseDto>(mapper.Map<List<EmployeeResponseDto>>(rows), page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EmployeeDetailDto>> GetById(int id)
    {
        var entity = await db.Employees.AsNoTracking().Include(e => e.Department).FirstOrDefaultAsync(e => e.Id == id);
        return entity is null ? NotFound() : Ok(mapper.Map<EmployeeDetailDto>(entity));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateEmployeeDto dto)
    {
        var entity = await db.Employees.FindAsync(id);
        if (entity is null) return NotFound();
        if (!await db.Departments.AnyAsync(d => d.Id == dto.DepartmentId)) return BadRequest(new { message = "DepartmentId does not exist." });
        mapper.Map(dto, entity);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { message = "An employee with this email already exists." }); }
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.Employees.FindAsync(id);
        if (entity is null) return NotFound();
        db.Employees.Remove(entity);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/image")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        var employee = await db.Employees.FindAsync(id);
        if (employee is null) return NotFound();
        if (file is null || file.Length == 0) return BadRequest(new { message = "Select an image file." });
        if (!ImageUploadValidator.TryGetStoredExtension(file, out var ext))
            return BadRequest(new { message = "Choose a valid JPG, PNG, GIF, WEBP, BMP, or AVIF photo up to 20 MB." });
        var uploads = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads");
        Directory.CreateDirectory(uploads);
        var fileName = $"emp_{Guid.NewGuid():N}{ext}";
        await using (var stream = System.IO.File.Create(Path.Combine(uploads, fileName))) await file.CopyToAsync(stream);
        employee.ImageUrl = $"/uploads/{fileName}";
        await db.SaveChangesAsync();
        return Ok(new { imageUrl = employee.ImageUrl });
    }
}
