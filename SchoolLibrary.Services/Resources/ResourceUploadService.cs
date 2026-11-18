using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Storage;

namespace SchoolLibrary.Services.Resources;

public class ResourceUploadService : IResourceUploadService
{
    private const string NotAllowedMessage = "Разрешени са само файлове PDF, EPUB и Markdown (.pdf, .epub, .md).";
    private const string NoFileMessage = "Изберете файл за качване.";
    private const string DuplicateMessage = "Този файл вече е качен в библиотеката.";

    // "%PDF-" and the ZIP local-file header (an EPUB is a ZIP container)
    private static readonly byte[] PdfMagic = { 0x25, 0x50, 0x44, 0x46, 0x2D };
    private static readonly byte[] ZipMagic = { 0x50, 0x4B, 0x03, 0x04 };

    private readonly ApplicationDbContext _context;
    private readonly IFileStorage _storage;

    public ResourceUploadService(ApplicationDbContext context, IFileStorage storage)
    {
        _context = context;
        _storage = storage;
    }

    public async Task<ServiceResult<Resource>> UploadAsync(
        ResourceInput input, UploadedFile file, string? ownerId, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length <= 0)
        {
            return Invalid(NoFileMessage);
        }

        if (file.Length > UploadConstraints.MaxFileSizeBytes)
        {
            return Invalid(TooLargeMessage);
        }

        var bytes = await ReadBytesAsync(file.Content, cancellationToken);
        if (bytes.Length == 0)
        {
            return Invalid(NoFileMessage);
        }

        // The declared length may be wrong; check the real size too
        if (bytes.Length > UploadConstraints.MaxFileSizeBytes)
        {
            return Invalid(TooLargeMessage);
        }

        var type = DetectType(file.FileName, bytes);
        if (type == null)
        {
            return Invalid(NotAllowedMessage);
        }

        // An uploaded resource has no link: force the kind so the field validation skips the URL rules
        input.Kind = ResourceKind.Upload;
        input.Url = string.Empty;

        var fieldError = ResourceService.Validate(input);
        if (fieldError != null)
        {
            return Invalid(fieldError);
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (await _context.ResourceFiles.AnyAsync(f => f.Sha256 == hash, cancellationToken))
        {
            return ServiceResult<Resource>.Failure(ServiceError.Conflict, DuplicateMessage);
        }

        var categoryIds = input.CategoryIds.Distinct().ToList();
        var categories = await _context.Categories.Where(c => categoryIds.Contains(c.Id)).ToListAsync(cancellationToken);
        if (categories.Count != categoryIds.Count)
        {
            return Invalid("Избран е несъществуващ предмет.");
        }

        var gradeIds = input.GradeLevelIds.Distinct().ToList();
        var grades = await _context.GradeLevels.Where(g => gradeIds.Contains(g.Id)).ToListAsync(cancellationToken);
        if (grades.Count != gradeIds.Count)
        {
            return Invalid("Избран е несъществуващ клас.");
        }

        var license = await _context.Licenses.FirstOrDefaultAsync(l => l.Id == input.LicenseId, cancellationToken);
        if (license == null)
        {
            return Invalid("Избран е несъществуващ лиценз.");
        }

        var key = $"resources/{Guid.NewGuid():N}{type.Extension}";
        await _storage.SaveAsync(key, new MemoryStream(bytes, writable: false), cancellationToken);

        try
        {
            var resource = new Resource
            {
                Title = input.Title.Trim(),
                Author = input.Author.Trim(),
                Source = string.IsNullOrWhiteSpace(input.Source) ? null : input.Source.Trim(),
                Description = input.Description?.Trim() ?? string.Empty,
                Url = string.Empty,
                NormalizedUrl = null,
                Kind = ResourceKind.Upload,
                CreatedOn = DateTime.UtcNow,
                OwnerId = ownerId,
                License = license,
                File = new ResourceFile
                {
                    StorageKey = key,
                    FileName = SafeFileName(file.FileName, type.Extension),
                    ContentType = type.ContentType,
                    SizeBytes = bytes.LongLength,
                    Sha256 = hash,
                    UploadedOn = DateTime.UtcNow
                }
            };

            foreach (var category in categories)
            {
                resource.Categories.Add(category);
            }

            foreach (var grade in grades)
            {
                resource.GradeLevels.Add(grade);
            }

            _context.Resources.Add(resource);
            await _context.SaveChangesAsync(cancellationToken);

            return ServiceResult<Resource>.Success(resource);
        }
        catch
        {
            // Never leave an orphan file behind when the database write fails
            await _storage.DeleteAsync(key, cancellationToken);
            throw;
        }
    }

    private static string TooLargeMessage =>
        $"Файлът не може да бъде по-голям от {UploadConstraints.MaxFileSizeMegabytes} МБ.";

    private static ServiceResult<Resource> Invalid(string message) =>
        ServiceResult<Resource>.Failure(ServiceError.Invalid, message);

    private static async Task<byte[]> ReadBytesAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    // The real type is decided from the bytes, not from the name or the declared content type
    private static FileType? DetectType(string fileName, byte[] bytes)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => StartsWith(bytes, PdfMagic) ? new FileType(".pdf", "application/pdf") : null,
            ".epub" => StartsWith(bytes, ZipMagic) ? new FileType(".epub", "application/epub+zip") : null,
            ".md" or ".markdown" => IsUtf8Text(bytes) ? new FileType(".md", "text/markdown; charset=utf-8") : null,
            _ => null
        };
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUtf8Text(byte[] bytes)
    {
        // A NUL byte means binary content, not text
        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            return false;
        }

        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string SafeFileName(string fileName, string extension)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = "материал" + extension;
        }

        return name.Length > ResourceFile.FileNameMaxLength
            ? name[..ResourceFile.FileNameMaxLength]
            : name;
    }

    private sealed record FileType(string Extension, string ContentType);
}
