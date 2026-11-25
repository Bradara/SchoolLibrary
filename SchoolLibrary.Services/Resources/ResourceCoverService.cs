using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
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
