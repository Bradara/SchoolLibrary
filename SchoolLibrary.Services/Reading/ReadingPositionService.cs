using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Reading;

public class ReadingPositionService : IReadingPositionService
{
    /// <summary>How many books per user keep their position.</summary>
    public const int MaxBooksPerUser = 5;

    public const string EpubContentType = EpubLocation.EpubContentType;

    public const string PdfContentType = PdfLocation.PdfContentType;

    /// <summary>Whether the reader of this file type keeps the place where the user stopped.</summary>
    public static bool KeepsPosition(string? contentType) =>
        contentType is EpubContentType or PdfContentType;

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

    public async Task<ServiceResult> SaveAsync(string userId, int resourceId, string? location, int percent)
    {
        location = location?.Trim();

        // The kind of the file decides what a valid location looks like
        var contentType = await _context.ResourceFiles
            .Where(f => f.ResourceId == resourceId)
            .Select(f => f.ContentType)
            .FirstOrDefaultAsync();
        var valid = contentType switch
        {
            EpubContentType => EpubLocation.IsValidCfi(location),
            PdfContentType => PdfLocation.IsValid(location),
            _ => (bool?)null
        };

        if (valid == null)
        {
            return ServiceResult.Failure(ServiceError.NotFound, "Книгата не е намерена.");
        }

        if (valid == false)
        {
            return ServiceResult.Failure(ServiceError.Invalid,
                contentType == PdfContentType ? "Невалидна страница в документа." : "Невалидна позиция в книгата.");
        }

        var position = await _context.ReadingPositions
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ResourceId == resourceId);
        if (position == null)
        {
            position = new ReadingPosition { UserId = userId, ResourceId = resourceId };
            _context.ReadingPositions.Add(position);
        }

        position.Cfi = location!;
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
}
