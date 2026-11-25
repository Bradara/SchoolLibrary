using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Reading;
using SchoolLibrary.Services.Storage;

namespace SchoolLibrary.Services.Resources;

/// <summary>Cover pictures of uploaded EPUB books: taken out of the book and kept in the file storage.</summary>
public static class ResourceCovers
{
    public const string KeyPrefix = "covers/";

    /// <summary>Whether the uploaded file is an EPUB book, the only kind of file with a cover.</summary>
    public static bool IsBook(ResourceFile file) => file.ContentType == EpubLocation.EpubContentType;

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
