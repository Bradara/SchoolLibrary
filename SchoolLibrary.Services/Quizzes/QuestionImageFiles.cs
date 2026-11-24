using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Storage;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Type detection, storage keys and clean-up of the files behind question pictures.</summary>
public static class QuestionImageFiles
{
    public const string KeyPrefix = "questions/";

    /// <summary>Extensions offered by the upload form (used for the input's "accept" attribute).</summary>
    public const string AcceptAttribute = ".png,.jpg,.jpeg,.gif,.webp";

    /// <summary>
    /// The picture type recognized from the first bytes: PNG, JPEG, GIF or WebP. Anything else (SVG,
    /// HTML, icons) gives null, whatever the file name or declared type says.
    /// </summary>
    public static (string ContentType, string Extension)? DetectType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ("image/png", ".png");
        if (bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return ("image/jpeg", ".jpg");
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return ("image/gif", ".gif");
        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) return ("image/webp", ".webp");
        return null;
    }

    // Only the name part is kept (no folders), with the extension of the real type
    public static string CleanFileName(string? fileName, string extension)
    {
        var name = Path.GetFileNameWithoutExtension(Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')));
        name = new string(name.Where(c => !char.IsControl(c) && c != '"' && c != '/').ToArray()).Trim();
        if (name.Length == 0)
        {
            name = "image";
        }

        var maxName = QuestionImage.FileNameMaxLength - extension.Length;
        return (name.Length > maxName ? name[..maxName] : name) + extension;
    }

    /// <summary>A new key such as "questions/{guid}.png".</summary>
    public static string NewKey(string extension) => $"{KeyPrefix}{Guid.NewGuid():N}{extension}";

    /// <summary>The file id in a key made by <see cref="NewKey"/>, or null for another key.</summary>
    public static Guid? FileIdOf(string storageKey)
    {
        if (!storageKey.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(storageKey[KeyPrefix.Length..]);
        return Guid.TryParseExact(name, "N", out var id) ? id : null;
    }

    /// <summary>The 1–10 numbers not used by the pictures yet, lowest first.</summary>
    public static int? NextFreeNumber(IEnumerable<int> used)
    {
        var taken = used.ToHashSet();
        for (var n = 1; n <= QuestionImage.MaxPerQuestion; n++)
        {
            if (!taken.Contains(n))
            {
                return n;
            }
        }

        return null;
    }

    /// <summary>
    /// Collapses the description to one line (null when blank). Returns false when it is still longer
    /// than the column allows; the text is never cut.
    /// </summary>
    public static bool TryCleanAltText(string? altText, out string? cleaned)
    {
        cleaned = string.IsNullOrWhiteSpace(altText)
            ? null
            : string.Join(' ', altText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned == null || cleaned.Length <= QuestionImage.AltTextMaxLength;
    }

    /// <summary>
    /// Marks the picture rows for removal (saved with the caller's SaveChanges) and returns their keys,
    /// to pass to <see cref="DeleteUnusedAsync"/> afterwards. The rows are removed explicitly rather than
    /// left to the cascade, so the keys are known and every database provider removes them.
    /// </summary>
    public static async Task<IReadOnlyList<string>> RemoveRowsAsync(ApplicationDbContext context,
        IQueryable<QuestionImage> images, CancellationToken cancellationToken = default)
    {
        var rows = await images.ToListAsync(cancellationToken);
        context.QuestionImages.RemoveRange(rows);
        return rows.Select(i => i.StorageKey).Distinct().ToList();
    }

    /// <summary>
    /// Deletes the stored files of the given keys that no picture row refers to any more. Call it after
    /// the rows were removed (copies of a question may share a file).
    /// </summary>
    public static async Task DeleteUnusedAsync(ApplicationDbContext context, IFileStorage storage,
        IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var candidates = keys.Distinct().ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var stillUsed = await context.QuestionImages
            .Where(i => candidates.Contains(i.StorageKey))
            .Select(i => i.StorageKey)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var key in candidates.Except(stillUsed))
        {
            await storage.DeleteAsync(key, cancellationToken);
        }
    }
}
