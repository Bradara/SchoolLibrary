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
        ResourceInput input, UploadedFile file, string? ownerId, UploadedFile? pdfCover = null,
        CancellationToken cancellationToken = default)
    {
        var (bytes, type, fileError) = await CheckFileAsync(file, cancellationToken);
        if (fileError != null)
        {
            return Invalid(fileError);
        }

        // An uploaded resource has no link: force the kind so the field validation skips the URL rules
        input.Kind = ResourceKind.Upload;
        input.Url = string.Empty;

        var fieldError = ResourceService.Validate(input);
        if (fieldError != null)
        {
            return Invalid(fieldError);
        }

        var hash = HashOf(bytes!);
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

        var key = $"resources/{Guid.NewGuid():N}{type!.Extension}";
        await _storage.SaveAsync(key, new MemoryStream(bytes!, writable: false), cancellationToken);
        string? coverKey = null;

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
                Audience = ResourceVisibility.Clean(input.Audience),
                CreatedOn = DateTime.UtcNow,
                OwnerId = ownerId,
                License = license,
                File = new ResourceFile
                {
                    StorageKey = key,
                    FileName = SafeFileName(file.FileName, type.Extension),
                    ContentType = type.ContentType,
                    SizeBytes = bytes!.LongLength,
                    Sha256 = hash,
                    UploadedOn = DateTime.UtcNow
                }
            };

            coverKey = await AttachCoverAsync(resource.File, bytes, pdfCover, cancellationToken);

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
            if (coverKey != null)
            {
                await _storage.DeleteAsync(coverKey, cancellationToken);
            }

            throw;
        }
    }

    public async Task<ServiceResult<Resource>> ReplaceFileAsync(
        int resourceId, UploadedFile file, string userId, bool isAdministrator, UploadedFile? pdfCover = null,
        CancellationToken cancellationToken = default)
    {
        var resource = await _context.Resources
            .Include(r => r.File)
            .FirstOrDefaultAsync(r => r.Id == resourceId, cancellationToken);
        if (resource == null || resource.Kind != ResourceKind.Upload || resource.File == null)
        {
            return ServiceResult<Resource>.Failure(ServiceError.NotFound, "Каченият материал не е намерен.");
        }

        if (!ResourcePermissions.CanEdit(resource, userId, isAdministrator))
        {
            return ServiceResult<Resource>.Failure(ServiceError.Forbidden, "Нямате право да променяте този материал.");
        }

        var (bytes, type, fileError) = await CheckFileAsync(file, cancellationToken);
        if (fileError != null)
        {
            return Invalid(fileError);
        }

        var hash = HashOf(bytes!);
        if (hash == resource.File.Sha256)
        {
            return ServiceResult<Resource>.Failure(ServiceError.Conflict, "Това е същият файл, който вече е качен.");
        }

        if (await _context.ResourceFiles.AnyAsync(f => f.Sha256 == hash, cancellationToken))
        {
            return ServiceResult<Resource>.Failure(ServiceError.Conflict, DuplicateMessage);
        }

        var stored = resource.File;
        var oldKey = stored.StorageKey;
        var oldCoverKey = stored.CoverKey;
        var key = $"resources/{Guid.NewGuid():N}{type!.Extension}";
        await _storage.SaveAsync(key, new MemoryStream(bytes!, writable: false), cancellationToken);
        string? coverKey = null;

        try
        {
            stored.StorageKey = key;
            stored.FileName = SafeFileName(file.FileName, type.Extension);
            stored.ContentType = type.ContentType;
            stored.SizeBytes = bytes!.LongLength;
            stored.Sha256 = hash;
            stored.UploadedOn = DateTime.UtcNow;
            // The cover of the old file does not belong to the new one
            stored.CoverKey = null;
            stored.CoverContentType = null;
            coverKey = await AttachCoverAsync(stored, bytes, pdfCover, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The row still points to the old file: the new one would be an orphan
            await _storage.DeleteAsync(key, cancellationToken);
            if (coverKey != null)
            {
                await _storage.DeleteAsync(coverKey, cancellationToken);
            }

            throw;
        }

        // Only now, when the row points to the new file, the old file and its cover go
        await _storage.DeleteAsync(oldKey, cancellationToken);
        if (oldCoverKey != null)
        {
            await _storage.DeleteAsync(oldCoverKey, cancellationToken);
        }

        return ServiceResult<Resource>.Success(resource);
    }

    // The checks every uploaded file goes through: present, at most the size limit (declared and real), and an
    // allowed type recognized from its bytes
    private static async Task<(byte[]? Bytes, FileType? Type, string? Error)> CheckFileAsync(
        UploadedFile? file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length <= 0)
        {
            return (null, null, NoFileMessage);
        }

        if (file.Length > UploadConstraints.MaxFileSizeBytes)
        {
            return (null, null, TooLargeMessage);
        }

        var bytes = await ReadBytesAsync(file.Content, cancellationToken);
        if (bytes.Length == 0)
        {
            return (null, null, NoFileMessage);
        }

        // The declared length may be wrong; check the real size too
        if (bytes.Length > UploadConstraints.MaxFileSizeBytes)
        {
            return (null, null, TooLargeMessage);
        }

        var type = DetectType(file.FileName, bytes);
        return type == null ? (null, null, NotAllowedMessage) : (bytes, type, null);
    }

    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // A book keeps the cover from inside it; a PDF the first page drawn by the browser (a picture that is not
    // accepted is left out). Returns the stored key, or null when the file has no cover.
    private async Task<string?> AttachCoverAsync(
        ResourceFile file, byte[] bytes, UploadedFile? pdfCover, CancellationToken cancellationToken)
    {
        if (ResourceCovers.IsBook(file))
        {
            return await ResourceCovers.AttachAsync(file, bytes, _storage, cancellationToken);
        }

        if (ResourceCovers.IsPdf(file) && pdfCover != null)
        {
            var picture = await ReadPictureAsync(pdfCover, cancellationToken);
            return await ResourceCovers.AttachPdfCoverAsync(file, picture, _storage, cancellationToken);
        }

        return null;
    }

    private static string TooLargeMessage =>
        $"Файлът не може да бъде по-голям от {UploadConstraints.MaxFileSizeMegabytes} МБ.";

    private static ServiceResult<Resource> Invalid(string message) =>
        ServiceResult<Resource>.Failure(ServiceError.Invalid, message);

    // At most one byte over the limit is read, so a large picture costs nothing and is then refused
    private static async Task<byte[]?> ReadPictureAsync(UploadedFile picture, CancellationToken cancellationToken)
    {
        if (picture.Length <= 0 || picture.Length > ResourceCovers.MaxPdfCoverBytes)
        {
            return null;
        }

        var buffer = new byte[ResourceCovers.MaxPdfCoverBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length
               && (read = await picture.Content.ReadAsync(buffer.AsMemory(total), cancellationToken)) > 0)
        {
            total += read;
        }

        return total is > 0 and <= ResourceCovers.MaxPdfCoverBytes ? buffer[..total] : null;
    }

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
