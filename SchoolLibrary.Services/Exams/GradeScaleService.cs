using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Services.Exams;

public class GradeScaleService : IGradeScaleService
{
    private readonly ApplicationDbContext _context;

    public GradeScaleService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<GradeScaleListItem>> GetForOwnerAsync(string? ownerId)
    {
        var scales = await _context.GradeScales.AsNoTracking()
            .Where(s => ownerId == null || s.OwnerId == ownerId)
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .ToListAsync();

        var ids = scales.Select(s => s.Id).ToList();
        var counts = await _context.ExamAssignments
            .Where(e => e.GradeScaleId != null && ids.Contains(e.GradeScaleId.Value))
            .GroupBy(e => e.GradeScaleId!.Value)
            .Select(g => new { ScaleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ScaleId, g => g.Count);

        return scales.Select(s => new GradeScaleListItem(s, counts.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<ServiceResult<GradeScale>> GetAsync(int id, string userId, bool isAdministrator)
    {
        var scale = await _context.GradeScales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        return Denied(scale, userId, isAdministrator) ?? ServiceResult<GradeScale>.Success(scale!);
    }

    public async Task<ServiceResult<GradeScale>> CreateAsync(GradeScaleInput input, string ownerId)
    {
        var name = Clean(input.Name);
        var error = await ValidateAsync(name, input, ownerId, null);
        if (error != null)
        {
            return error;
        }

        var scale = new GradeScale { OwnerId = ownerId, CreatedOn = DateTime.UtcNow };
        Apply(scale, name, input);
        _context.GradeScales.Add(scale);
        await _context.SaveChangesAsync();

        return ServiceResult<GradeScale>.Success(scale);
    }

    public async Task<ServiceResult<GradeScale>> UpdateAsync(int id, GradeScaleInput input, string userId, bool isAdministrator)
    {
        var scale = await _context.GradeScales.FirstOrDefaultAsync(s => s.Id == id);
        var denied = Denied(scale, userId, isAdministrator);
        if (denied != null)
        {
            return denied;
        }

        var name = Clean(input.Name);
        var error = await ValidateAsync(name, input, scale!.OwnerId, scale.Id);
        if (error != null)
        {
            return error;
        }

        Apply(scale, name, input);
        await _context.SaveChangesAsync();

        return ServiceResult<GradeScale>.Success(scale);
    }

    public async Task<ServiceResult<GradeScale>> DeleteAsync(int id, string userId, bool isAdministrator)
    {
        var scale = await _context.GradeScales.FirstOrDefaultAsync(s => s.Id == id);
        var denied = Denied(scale, userId, isAdministrator);
        if (denied != null)
        {
            return denied;
        }

        var used = await _context.ExamAssignments.CountAsync(e => e.GradeScaleId == id);
        if (used > 0)
        {
            var exams = used == 1 ? "1 изпит" : $"{used} изпита";
            return ServiceResult<GradeScale>.Failure(ServiceError.Conflict,
                $"Скалата се използва от {exams}. Изберете друга скала за тях и опитайте отново.");
        }

        _context.GradeScales.Remove(scale!);
        await _context.SaveChangesAsync();

        return ServiceResult<GradeScale>.Success(scale!);
    }

    // The owner of a scale manages it, and an administrator any scale; null means allowed
    private static ServiceResult<GradeScale>? Denied(GradeScale? scale, string userId, bool isAdministrator)
    {
        if (scale == null)
        {
            return ServiceResult<GradeScale>.Failure(ServiceError.NotFound, "Скалата не е намерена.");
        }

        var isOwner = scale.OwnerId != null && !string.IsNullOrEmpty(userId) && scale.OwnerId == userId;
        return isOwner || isAdministrator
            ? null
            : ServiceResult<GradeScale>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
    }

    private async Task<ServiceResult<GradeScale>?> ValidateAsync(string name, GradeScaleInput input, string? ownerId, int? exceptId)
    {
        if (name.Length == 0)
        {
            return ServiceResult<GradeScale>.Failure(ServiceError.Invalid, "Въведете име на скалата.");
        }

        if (name.Length > GradeScale.NameMaxLength)
        {
            return ServiceResult<GradeScale>.Failure(ServiceError.Invalid,
                $"Името не може да бъде по-дълго от {GradeScale.NameMaxLength} символа.");
        }

        var thresholds = ExamGrading.ValidateThresholds(input.Three, input.Four, input.Five, input.Six);
        if (thresholds != null)
        {
            return ServiceResult<GradeScale>.Failure(ServiceError.Invalid, thresholds);
        }

        var lowered = name.ToLower();
        var taken = await _context.GradeScales.AnyAsync(s =>
            s.OwnerId == ownerId && s.Id != exceptId && s.Name.ToLower() == lowered);
        return taken
            ? ServiceResult<GradeScale>.Failure(ServiceError.Conflict, $"Вече имате скала „{name}“.")
            : null;
    }

    private static void Apply(GradeScale scale, string name, GradeScaleInput input)
    {
        scale.Name = name;
        scale.Three = input.Three;
        scale.Four = input.Four;
        scale.Five = input.Five;
        scale.Six = input.Six;
    }

    // Outer and repeated spaces are dropped
    private static string Clean(string? name)
        => string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
