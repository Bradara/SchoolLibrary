using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Reading;

public class HighlightService : IHighlightService
{
    /// <summary>How many highlights a user can keep in one book.</summary>
    public const int MaxPerBook = 500;

    /// <summary>The colors of the reader; the database accepts only these.</summary>
    public static readonly IReadOnlyList<string> Colors = new[] { "yellow", "green", "blue", "pink" };

    public const string DefaultColor = "yellow";

    private readonly ApplicationDbContext _context;

    public HighlightService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<HighlightInfo>> GetAsync(string userId, int resourceId)
    {
        return await _context.Highlights
            .AsNoTracking()
            .Where(h => h.UserId == userId && h.ResourceId == resourceId)
            .OrderBy(h => h.Percent)
            .ThenBy(h => h.CreatedOn)
            .ThenBy(h => h.Id)
            .Select(h => new HighlightInfo(h.Id, h.Cfi, h.Text, h.Color, h.Note, h.Percent))
            .ToListAsync();
    }

    public async Task<ServiceResult<HighlightInfo>> AddAsync(string userId, int resourceId, HighlightInput input)
    {
        var cfi = input.Cfi?.Trim();
        if (!EpubLocation.IsValidCfi(cfi))
        {
            return ServiceResult<HighlightInfo>.Failure(ServiceError.Invalid, "Невалидна позиция в книгата.");
        }

        var text = ReaderText.OneLine(input.Text, Highlight.TextMaxLength);
        if (text.Length == 0)
        {
            return ServiceResult<HighlightInfo>.Failure(ServiceError.Invalid, "Изберете текст за подчертаване.");
        }

        if (!TryCleanNote(input.Note, out var note, out var noteError))
        {
            return ServiceResult<HighlightInfo>.Failure(ServiceError.Invalid, noteError);
        }

        if (!await EpubLocation.IsBookAsync(_context, resourceId))
        {
            return ServiceResult<HighlightInfo>.Failure(ServiceError.NotFound, "Книгата не е намерена.");
        }

        var mine = _context.Highlights.Where(h => h.UserId == userId && h.ResourceId == resourceId);

        var existing = await mine.AsNoTracking().FirstOrDefaultAsync(h => h.Cfi == cfi);
        if (existing != null)
        {
            return ServiceResult<HighlightInfo>.Success(ToInfo(existing));
        }

        if (await mine.CountAsync() >= MaxPerBook)
        {
            return ServiceResult<HighlightInfo>.Failure(ServiceError.Conflict,
                $"Можете да имате най-много {MaxPerBook} подчертавания в една книга.");
        }

        var now = DateTime.UtcNow;
        var highlight = new Highlight
        {
            UserId = userId,
            ResourceId = resourceId,
            Cfi = cfi!,
            Text = text,
            Color = CleanColor(input.Color),
            Note = note,
            Percent = Math.Clamp(input.Percent, 0, 100),
            CreatedOn = now,
            UpdatedOn = now
        };
        _context.Highlights.Add(highlight);
        await _context.SaveChangesAsync();

        return ServiceResult<HighlightInfo>.Success(ToInfo(highlight));
    }

    public async Task<ServiceResult<HighlightInfo>> UpdateAsync(string userId, int highlightId, string? color, string? note)
    {
        if (!TryCleanNote(note, out var cleanNote, out var noteError))
        {
            return ServiceResult<HighlightInfo>.Failure(ServiceError.Invalid, noteError);
        }

        var highlight = await _context.Highlights.FirstOrDefaultAsync(h => h.Id == highlightId && h.UserId == userId);
        if (highlight == null)
        {
            return ServiceResult<HighlightInfo>.Failure(ServiceError.NotFound, "Подчертаването не е намерено.");
        }

        highlight.Color = CleanColor(color ?? highlight.Color);
        highlight.Note = cleanNote;
        highlight.UpdatedOn = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return ServiceResult<HighlightInfo>.Success(ToInfo(highlight));
    }

    public async Task<ServiceResult> RemoveAsync(string userId, int highlightId)
    {
        var highlight = await _context.Highlights.FirstOrDefaultAsync(h => h.Id == highlightId && h.UserId == userId);
        if (highlight == null)
        {
            return ServiceResult.Failure(ServiceError.NotFound, "Подчертаването не е намерено.");
        }

        _context.Highlights.Remove(highlight);
        await _context.SaveChangesAsync();
        return ServiceResult.Success();
    }

    private static HighlightInfo ToInfo(Highlight h) => new(h.Id, h.Cfi, h.Text, h.Color, h.Note, h.Percent);

    // An unknown color (an old page, a tampered request) falls back to yellow instead of failing
    public static string CleanColor(string? color)
    {
        var value = color?.Trim().ToLowerInvariant();
        return value != null && Colors.Contains(value) ? value : DefaultColor;
    }

    // The note keeps its lines; an empty note is no note. A note that is too long is refused, not cut:
    // the user typed it and would lose the end without noticing.
    public static bool TryCleanNote(string? note, out string? clean, out string error)
    {
        clean = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(note))
        {
            return true;
        }

        var text = note.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (text.Length > Highlight.NoteMaxLength)
        {
            error = $"Бележката може да е до {Highlight.NoteMaxLength} знака.";
            return false;
        }

        if (text.Any(c => char.IsControl(c) && c != '\n' && c != '\t'))
        {
            error = "Бележката съдържа невалидни знаци.";
            return false;
        }

        clean = text;
        return true;
    }
}
