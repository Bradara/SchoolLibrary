using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Reading;

/// <summary>Checks shared by the reading position and the bookmarks of an uploaded EPUB.</summary>
public static class EpubLocation
{
    public const string EpubContentType = "application/epub+zip";

    private const string CfiPrefix = "epubcfi(";

    /// <summary>
    /// Whether the text has the shape of an EPUB location ("epubcfi(...)"). Only the shape is checked: the reader
    /// asks epub.js to open it, which ignores a location it cannot find.
    /// </summary>
    public static bool IsValidCfi(string? cfi) =>
        !string.IsNullOrEmpty(cfi)
        && cfi.Length <= ReadingPosition.CfiMaxLength
        && cfi.StartsWith(CfiPrefix, StringComparison.Ordinal)
        && cfi.EndsWith(')')
        && !cfi.Any(char.IsControl);

    /// <summary>Whether the resource is an uploaded EPUB book.</summary>
    public static Task<bool> IsBookAsync(ApplicationDbContext context, int resourceId) =>
        context.ResourceFiles.AnyAsync(f => f.ResourceId == resourceId && f.ContentType == EpubContentType);
}
