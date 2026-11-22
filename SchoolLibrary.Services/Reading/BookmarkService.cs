using System.Text;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Reading;

public class BookmarkService : IBookmarkService
{
    /// <summary>How many bookmarks a user can keep in one book.</summary>
    public const int MaxPerBook = 50;

    public const string DefaultLabel = "Отметка";

    private readonly ApplicationDbContext _context;

    public BookmarkService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<BookmarkInfo>> GetAsync(string userId, int resourceId)
    {
        return await _context.Bookmarks
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.ResourceId == resourceId)
            .OrderBy(b => b.Percent)
            .ThenBy(b => b.CreatedOn)
            .ThenBy(b => b.Id)
            .Select(b => new BookmarkInfo(b.Id, b.Cfi, b.Label, b.Percent))
            .ToListAsync();
    }

    public async Task<ServiceResult<BookmarkInfo>> AddAsync(string userId, int resourceId, string? cfi, string? label, int percent)
    {
        cfi = cfi?.Trim();
        if (!EpubLocation.IsValidCfi(cfi))
        {
            return ServiceResult<BookmarkInfo>.Failure(ServiceError.Invalid, "Невалидна позиция в книгата.");
        }

        if (!await EpubLocation.IsBookAsync(_context, resourceId))
        {
            return ServiceResult<BookmarkInfo>.Failure(ServiceError.NotFound, "Книгата не е намерена.");
        }

        var mine = _context.Bookmarks.Where(b => b.UserId == userId && b.ResourceId == resourceId);

        var existing = await mine.AsNoTracking().FirstOrDefaultAsync(b => b.Cfi == cfi);
        if (existing != null)
        {
            return ServiceResult<BookmarkInfo>.Success(ToInfo(existing));
        }

        if (await mine.CountAsync() >= MaxPerBook)
        {
            return ServiceResult<BookmarkInfo>.Failure(ServiceError.Conflict,
                $"Можете да имате най-много {MaxPerBook} отметки в една книга. Изтрийте някоя от старите.");
        }

        var bookmark = new Bookmark
        {
            UserId = userId,
            ResourceId = resourceId,
            Cfi = cfi!,
            Label = CleanLabel(label),
            Percent = Math.Clamp(percent, 0, 100),
            CreatedOn = DateTime.UtcNow
        };
        _context.Bookmarks.Add(bookmark);
        await _context.SaveChangesAsync();

        return ServiceResult<BookmarkInfo>.Success(ToInfo(bookmark));
    }

    public async Task<ServiceResult> RemoveAsync(string userId, int bookmarkId)
    {
        var bookmark = await _context.Bookmarks.FirstOrDefaultAsync(b => b.Id == bookmarkId && b.UserId == userId);
        if (bookmark == null)
        {
            return ServiceResult.Failure(ServiceError.NotFound, "Отметката не е намерена.");
        }

        _context.Bookmarks.Remove(bookmark);
        await _context.SaveChangesAsync();
        return ServiceResult.Success();
    }

    private static BookmarkInfo ToInfo(Bookmark b) => new(b.Id, b.Cfi, b.Label, b.Percent);

    // The label comes from the page text: one line, no control characters, cut to the column length
    public static string CleanLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return DefaultLabel;
        }

        var text = new StringBuilder(label.Length);
        var space = false;
        foreach (var c in label)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = text.Length > 0;
                continue;
            }

            if (space)
            {
                text.Append(' ');
                space = false;
            }

            text.Append(c);
        }

        if (text.Length == 0)
        {
            return DefaultLabel;
        }

        if (text.Length > Bookmark.LabelMaxLength)
        {
            text.Length = Bookmark.LabelMaxLength - 1;
            text.Append('…');
        }

        return text.ToString();
    }
}
