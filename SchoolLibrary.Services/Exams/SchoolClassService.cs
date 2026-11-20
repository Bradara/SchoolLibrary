using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Exams;

public class SchoolClassService : ISchoolClassService
{
    public const int MaxNameLength = 50;

    private readonly ApplicationDbContext _context;

    public SchoolClassService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<SchoolClassListItem>> GetForOwnerAsync(string? ownerId)
    {
        var classes = await _context.SchoolClasses.AsNoTracking()
            .Where(c => ownerId == null || c.OwnerId == ownerId)
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .ToListAsync();

        var ids = classes.Select(c => c.Id).ToList();
        var counts = await _context.ExamAssignments
            .Where(e => ids.Contains(e.SchoolClassId))
            .GroupBy(e => e.SchoolClassId)
            .Select(g => new { ClassId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ClassId, g => g.Count);

        return classes.Select(c => new SchoolClassListItem(c, counts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<SchoolClass?> GetByIdAsync(int id)
        => await _context.SchoolClasses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);

    public async Task<ServiceResult<SchoolClass>> CreateAsync(string name, string ownerId)
    {
        var clean = Clean(name);
        var error = await ValidateNameAsync(clean, ownerId, null);
        if (error != null)
        {
            return error;
        }

        var schoolClass = new SchoolClass { Name = clean, OwnerId = ownerId, CreatedOn = DateTime.UtcNow };
        _context.SchoolClasses.Add(schoolClass);
        await _context.SaveChangesAsync();

        return ServiceResult<SchoolClass>.Success(schoolClass);
    }

    public async Task<ServiceResult<SchoolClass>> RenameAsync(int id, string name, string userId, bool isAdministrator)
    {
        var schoolClass = await _context.SchoolClasses.FirstOrDefaultAsync(c => c.Id == id);
        if (schoolClass == null)
        {
            return ServiceResult<SchoolClass>.Failure(ServiceError.NotFound, "Класът не е намерен.");
        }

        if (!ExamPermissions.CanManage(schoolClass, userId, isAdministrator))
        {
            return ServiceResult<SchoolClass>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        var clean = Clean(name);
        var error = await ValidateNameAsync(clean, schoolClass.OwnerId, schoolClass.Id);
        if (error != null)
        {
            return error;
        }

        schoolClass.Name = clean;
        await _context.SaveChangesAsync();

        return ServiceResult<SchoolClass>.Success(schoolClass);
    }

    public async Task<ServiceResult<SchoolClass>> DeleteAsync(int id, string userId, bool isAdministrator)
    {
        var schoolClass = await _context.SchoolClasses.FirstOrDefaultAsync(c => c.Id == id);
        if (schoolClass == null)
        {
            return ServiceResult<SchoolClass>.Failure(ServiceError.NotFound, "Класът не е намерен.");
        }

        if (!ExamPermissions.CanManage(schoolClass, userId, isAdministrator))
        {
            return ServiceResult<SchoolClass>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        // Cascade removes the class's exams and their attempts
        _context.SchoolClasses.Remove(schoolClass);
        await _context.SaveChangesAsync();

        return ServiceResult<SchoolClass>.Success(schoolClass);
    }

    private async Task<ServiceResult<SchoolClass>?> ValidateNameAsync(string name, string? ownerId, int? exceptId)
    {
        if (name.Length == 0)
        {
            return ServiceResult<SchoolClass>.Failure(ServiceError.Invalid, "Въведете име на класа.");
        }

        if (name.Length > MaxNameLength)
        {
            return ServiceResult<SchoolClass>.Failure(ServiceError.Invalid,
                $"Името не може да бъде по-дълго от {MaxNameLength} символа.");
        }

        var lowered = name.ToLower();
        var taken = await _context.SchoolClasses.AnyAsync(c =>
            c.OwnerId == ownerId && c.Id != exceptId && c.Name.ToLower() == lowered);
        if (taken)
        {
            return ServiceResult<SchoolClass>.Failure(ServiceError.Conflict, $"Вече имате клас „{name}“.");
        }

        return null;
    }

    // "5 а" and "5а" stay different, but outer and repeated spaces are dropped
    private static string Clean(string? name)
        => string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
