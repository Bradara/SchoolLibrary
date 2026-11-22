using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Reading;

/// <summary>The passages a signed-in user highlighted in an uploaded book (EPUB), with optional notes.</summary>
public interface IHighlightService
{
    /// <summary>The user's highlights in this book, in reading order.</summary>
    Task<IReadOnlyList<HighlightInfo>> GetAsync(string userId, int resourceId);

    /// <summary>
    /// Highlights a passage. Highlighting the same passage again returns the existing highlight unchanged.
    /// At most <see cref="HighlightService.MaxPerBook"/> highlights per book.
    /// </summary>
    Task<ServiceResult<HighlightInfo>> AddAsync(string userId, int resourceId, HighlightInput input);

    /// <summary>Changes the color and the note of one of the user's own highlights.</summary>
    Task<ServiceResult<HighlightInfo>> UpdateAsync(string userId, int highlightId, string? color, string? note);

    /// <summary>Removes one of the user's own highlights; another user's highlight is "not found".</summary>
    Task<ServiceResult> RemoveAsync(string userId, int highlightId);
}

/// <summary>What the reader sends for a new highlight.</summary>
public record HighlightInput(string? Cfi, string? Text, string? Color, string? Note, int Percent);

/// <summary>A saved highlight.</summary>
public record HighlightInfo(int Id, string Cfi, string Text, string Color, string? Note, int Percent);
