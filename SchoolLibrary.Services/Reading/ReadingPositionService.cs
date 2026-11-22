using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Reading;

public class ReadingPositionService : IReadingPositionService
{
    /// <summary>How many books per user keep their position.</summary>
    public const int MaxBooksPerUser = 5;

    public const string EpubContentType = "application/epub+zip";

    private const string CfiPrefix = "epubcfi(";

    private readonly ApplicationDbContext _context;

    public ReadingPositionService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ReadingPositionInfo?> GetAsync(string userId, int resourceId)
    {
        return await _context.ReadingPositions
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.ResourceId == resourceId)
            .Select(p => new ReadingPositionInfo(p.Cfi, p.Percent, p.UpdatedOn))
            .FirstOrDefaultAsync();
    }

    public async Task<ServiceResult> SaveAsync(string userId, int resourceId, string? cfi, int percent)
    {
        cfi = cfi?.Trim();
        if (!IsValidCfi(cfi))
        {
            return ServiceResult.Failure(ServiceError.Invalid, "Невалидна позиция в книгата.");
        }

        var isBook = await _context.ResourceFiles
            .AnyAsync(f => f.ResourceId == resourceId && f.ContentType == EpubContentType);
        if (!isBook)
        {
            return ServiceResult.Failure(ServiceError.NotFound, "Книгата не е намерена.");
        }

        var position = await _context.ReadingPositions
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ResourceId == resourceId);
        if (position == null)
        {
            position = new ReadingPosition { UserId = userId, ResourceId = resourceId };
            _context.ReadingPositions.Add(position);
        }

        position.Cfi = cfi!;
        position.Percent = Math.Clamp(percent, 0, 100);
        position.UpdatedOn = DateTime.UtcNow;

        // Forget the books read longest ago, keeping this one and the next most recent ones
        var forgotten = await _context.ReadingPositions
            .Where(p => p.UserId == userId && p.ResourceId != resourceId)
            .OrderByDescending(p => p.UpdatedOn)
            .ThenByDescending(p => p.ResourceId)
            .Skip(MaxBooksPerUser - 1)
            .ToListAsync();
        _context.ReadingPositions.RemoveRange(forgotten);

        await _context.SaveChangesAsync();
        return ServiceResult.Success();
    }

    // Only the shape is checked: the reader asks epub.js to open it, which ignores a location it cannot find
    private static bool IsValidCfi(string? cfi) =>
        !string.IsNullOrEmpty(cfi)
        && cfi.Length <= ReadingPosition.CfiMaxLength
        && cfi.StartsWith(CfiPrefix, StringComparison.Ordinal)
        && cfi.EndsWith(')')
        && !cfi.Any(char.IsControl);
}
