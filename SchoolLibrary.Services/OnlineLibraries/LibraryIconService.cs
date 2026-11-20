using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.SiteIcons;
using SchoolLibrary.Services.Storage;

namespace SchoolLibrary.Services.OnlineLibraries;

/// <summary>A stored site icon, opened for sending to the browser.</summary>
public record LibraryIconFile(Stream Content, string ContentType);

/// <summary>Downloads, stores and serves the site icons of the online libraries.</summary>
public interface ILibraryIconService
{
    /// <summary>
    /// Downloads the icon of the library's site and stores it. Without <paramref name="force"/> nothing is
    /// downloaded when the stored icon already belongs to the current address. Fails with Invalid when the
    /// site offers no usable icon: then the chosen Bootstrap icon is shown.
    /// </summary>
    Task<ServiceResult<OnlineLibrary>> RefreshAsync(int libraryId, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>The stored icon of a library, or null when it has none.</summary>
    Task<LibraryIconFile?> OpenAsync(int libraryId, CancellationToken cancellationToken = default);

    /// <summary>Removes the stored icon file of a library that was deleted from the catalog.</summary>
    Task DeleteStoredAsync(OnlineLibrary library, CancellationToken cancellationToken = default);
}

public class LibraryIconService : ILibraryIconService
{
    public const string KeyPrefix = "libraries/";

    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/gif"] = ".gif",
        ["image/x-icon"] = ".ico",
        ["image/webp"] = ".webp"
    };

    private readonly ApplicationDbContext _context;
    private readonly ISiteIconFetcher _fetcher;
    private readonly IFileStorage _storage;

    public LibraryIconService(ApplicationDbContext context, ISiteIconFetcher fetcher, IFileStorage storage)
    {
        _context = context;
        _fetcher = fetcher;
        _storage = storage;
    }

    public async Task<ServiceResult<OnlineLibrary>> RefreshAsync(int libraryId, bool force = false, CancellationToken cancellationToken = default)
    {
        var library = await _context.OnlineLibraries.FirstOrDefaultAsync(l => l.Id == libraryId, cancellationToken);
        if (library == null)
        {
            return ServiceResult<OnlineLibrary>.Failure(ServiceError.NotFound, "Библиотеката не е намерена.");
        }

        var belongsToAddress = library.HasSiteIcon && library.SiteIconSourceUrl == library.Url;
        if (belongsToAddress && !force)
        {
            return ServiceResult<OnlineLibrary>.Success(library);
        }

        var oldKey = library.SiteIconKey;
        var icon = await _fetcher.FetchAsync(library.Url, cancellationToken);

        if (icon == null || !Extensions.TryGetValue(icon.ContentType, out var extension))
        {
            // The icon of another site must not stay on the card; a working one for the same address is kept
            if (oldKey != null && !belongsToAddress)
            {
                SetIcon(library, null, null, null);
                await _context.SaveChangesAsync(cancellationToken);
                await _storage.DeleteAsync(oldKey, cancellationToken);
            }

            return ServiceResult<OnlineLibrary>.Failure(ServiceError.Invalid,
                "Иконата на сайта не можа да бъде изтеглена. Показва се избраната иконка.");
        }

        var key = $"{KeyPrefix}{Guid.NewGuid():N}{extension}";
        using (var content = new MemoryStream(icon.Content, writable: false))
        {
            await _storage.SaveAsync(key, content, cancellationToken);
        }

        try
        {
            SetIcon(library, key, icon.ContentType, library.Url);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }

        // Only after the new key is saved, so the card never points to a missing file
        if (oldKey != null)
        {
            await _storage.DeleteAsync(oldKey, cancellationToken);
        }

        return ServiceResult<OnlineLibrary>.Success(library);
    }

    public async Task<LibraryIconFile?> OpenAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        var icon = await _context.OnlineLibraries
            .AsNoTracking()
            .Where(l => l.Id == libraryId && l.SiteIconKey != null && l.SiteIconContentType != null)
            .Select(l => new { l.SiteIconKey, l.SiteIconContentType })
            .FirstOrDefaultAsync(cancellationToken);
        if (icon == null) return null;

        var stream = await _storage.OpenReadAsync(icon.SiteIconKey!, cancellationToken);
        return stream == null ? null : new LibraryIconFile(stream, icon.SiteIconContentType!);
    }

    public async Task DeleteStoredAsync(OnlineLibrary library, CancellationToken cancellationToken = default)
    {
        if (library.SiteIconKey != null)
        {
            await _storage.DeleteAsync(library.SiteIconKey, cancellationToken);
        }
    }

    private static void SetIcon(OnlineLibrary library, string? key, string? contentType, string? sourceUrl)
    {
        library.SiteIconKey = key;
        library.SiteIconContentType = contentType;
        library.SiteIconSourceUrl = sourceUrl;
    }
}
