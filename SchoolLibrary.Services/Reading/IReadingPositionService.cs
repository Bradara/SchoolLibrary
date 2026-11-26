using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Reading;

/// <summary>Where a signed-in user stopped reading an uploaded book (EPUB) or document (PDF), kept on the server for every device.</summary>
public interface IReadingPositionService
{
    /// <summary>The saved position of the user in this book, or null when there is none.</summary>
    Task<ReadingPositionInfo?> GetAsync(string userId, int resourceId);

    /// <summary>
    /// Saves the position in an uploaded EPUB (a CFI, "epubcfi(...)") or PDF (a page, "pdfpage(12)"). Only the <see cref="ReadingPositionService.MaxBooksPerUser"/> most
    /// recently read books of the user are kept; older ones are forgotten.
    /// </summary>
    Task<ServiceResult> SaveAsync(string userId, int resourceId, string? location, int percent);
}

/// <summary>A saved reading position.</summary>
public record ReadingPositionInfo(string Cfi, int Percent, DateTime UpdatedOn);
