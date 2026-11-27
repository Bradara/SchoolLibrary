using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.Services.Reading;
using SchoolLibrary.Services.Storage;

namespace SchoolLibrary.Services.Resources;

/// <summary>
/// Cover pictures of uploaded files, kept in the file storage. An EPUB book carries its cover inside; a PDF has
/// none, so the browser draws its first page (pdf.js) and sends the picture, which is checked here.
/// </summary>
public static class ResourceCovers
{
    public const string KeyPrefix = "covers/";

    /// <summary>The largest first-page picture of a PDF that is accepted (the browser sends about 30–80 KB).</summary>
    public const int MaxPdfCoverBytes = 300 * 1024;

    /// <summary>Whether the uploaded file is an EPUB book, the only kind of file with a cover.</summary>
    public static bool IsBook(ResourceFile file) => file.ContentType == EpubLocation.EpubContentType;

    /// <summary>Whether the uploaded file is a PDF, whose cover is drawn by the browser.</summary>
    public static bool IsPdf(ResourceFile file) => file.ContentType == PdfLocation.PdfContentType;

    /// <summary>
    /// The type of a first-page picture sent for a PDF: PNG, JPEG or WebP recognized from the bytes, at most
    /// <see cref="MaxPdfCoverBytes"/>. Anything else (GIF, SVG, HTML, an empty or too large file) gives null.
    /// </summary>
    public static (string ContentType, string Extension)? PdfCoverType(byte[]? picture)
    {
        if (picture == null || picture.Length == 0 || picture.Length > MaxPdfCoverBytes)
        {
            return null;
        }

        var type = QuestionImageFiles.DetectType(picture);
        return type is { ContentType: not "image/gif" } ? type : null;
    }

    /// <summary>
    /// Stores the first-page picture of a PDF and records it on <paramref name="file"/>. Returns the new key, or
    /// null when the picture is not accepted (nothing is stored then). As with a book, the caller saves the row
    /// and deletes the key again if that fails.
    /// </summary>
    public static async Task<string?> AttachPdfCoverAsync(
        ResourceFile file, byte[]? picture, IFileStorage storage, CancellationToken cancellationToken = default)
    {
        if (!IsPdf(file) || PdfCoverType(picture) is not { } type)
        {
            return null;
        }

        var key = $"{KeyPrefix}{Guid.NewGuid():N}{type.Extension}";
        await storage.SaveAsync(key, new MemoryStream(picture!, writable: false), cancellationToken);
        file.CoverKey = key;
        file.CoverContentType = type.ContentType;
        return key;
    }

    /// <summary>
    /// Stores the cover of the book and records it on <paramref name="file"/>. Returns the new key, or null when
    /// the book has no usable cover (nothing is stored then). The caller saves the database row and deletes the
    /// key again if that fails.
    /// </summary>
    public static async Task<string?> AttachAsync(
        ResourceFile file, byte[] epub, IFileStorage storage, CancellationToken cancellationToken = default)
    {
        var cover = EpubMetadata.Read(epub).Cover;
        if (cover == null)
        {
            return null;
        }

        var key = $"{KeyPrefix}{Guid.NewGuid():N}{cover.Extension}";
        await storage.SaveAsync(key, new MemoryStream(cover.Bytes, writable: false), cancellationToken);
        file.CoverKey = key;
        file.CoverContentType = cover.ContentType;
        return key;
    }
}
