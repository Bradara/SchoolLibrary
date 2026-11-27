using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Reading;
using SchoolLibrary.Services.Storage;

namespace SchoolLibrary.Services.Resources;

public class ResourceCoverService : IResourceCoverService
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorage _storage;

    public ResourceCoverService(ApplicationDbContext context, IFileStorage storage)
    {
        _context = context;
        _storage = storage;
    }

    public Task<int> CountBooksWithoutCoverAsync(CancellationToken cancellationToken = default) =>
        BooksWithoutCover().CountAsync(cancellationToken);

    public async Task<CoverExtractionResult> ExtractMissingAsync(CancellationToken cancellationToken = default)
    {
        var books = await BooksWithoutCover().OrderBy(f => f.Id).ToListAsync(cancellationToken);
        int added = 0, withoutCover = 0, fileMissing = 0;

        foreach (var book in books)
        {
            var bytes = await ReadAsync(book.StorageKey, cancellationToken);
            if (bytes == null)
            {
                fileMissing++;
                continue;
            }

            var key = await ResourceCovers.AttachAsync(book, bytes, _storage, cancellationToken);
            if (key == null)
            {
                withoutCover++;
                continue;
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                added++;
            }
            catch
            {
                // The row keeps no cover, so the stored picture would be an orphan
                await _storage.DeleteAsync(key, cancellationToken);
                throw;
            }
        }

        return new CoverExtractionResult(books.Count, added, withoutCover, fileMissing);
    }

    public Task<int> CountPdfsWithoutCoverAsync(CancellationToken cancellationToken = default) =>
        PdfsWithoutCover().CountAsync(cancellationToken);

    public async Task<IReadOnlyList<PdfWithoutCover>> GetPdfsWithoutCoverAsync(CancellationToken cancellationToken = default) =>
        await PdfsWithoutCover()
            .OrderBy(f => f.Id)
            .Select(f => new PdfWithoutCover(f.ResourceId, f.Resource.Title))
            .ToListAsync(cancellationToken);

    public async Task<ServiceResult> SetPdfCoverAsync(
        int resourceId, byte[]? picture, CancellationToken cancellationToken = default)
    {
        var file = await _context.ResourceFiles.FirstOrDefaultAsync(
            f => f.ResourceId == resourceId && f.ContentType == PdfLocation.PdfContentType, cancellationToken);
        if (file == null)
        {
            return ServiceResult.Failure(ServiceError.NotFound, "Документът не е намерен.");
        }

        if (file.CoverKey != null)
        {
            return ServiceResult.Failure(ServiceError.Conflict, "Документът вече има обложка.");
        }

        var key = await ResourceCovers.AttachPdfCoverAsync(file, picture, _storage, cancellationToken);
        if (key == null)
        {
            return ServiceResult.Failure(ServiceError.Invalid,
                $"Обложката трябва да е PNG, JPEG или WebP до {ResourceCovers.MaxPdfCoverBytes / 1024} KB.");
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _storage.DeleteAsync(key, cancellationToken);
            throw;
        }

        return ServiceResult.Success();
    }

    private IQueryable<ResourceFile> PdfsWithoutCover() =>
        _context.ResourceFiles.Where(f =>
            f.ContentType == PdfLocation.PdfContentType && f.CoverKey == null);

    private IQueryable<ResourceFile> BooksWithoutCover() =>
        _context.ResourceFiles.Where(f => f.ContentType == EpubLocation.EpubContentType && f.CoverKey == null);

    // The whole book, at most the upload limit (a bigger file was not uploaded through the site)
    private async Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        await using var stream = await _storage.OpenReadAsync(key, cancellationToken);
        if (stream == null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > UploadConstraints.MaxFileSizeBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
