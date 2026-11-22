using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Reading;

/// <summary>The bookmarks a signed-in user keeps in an uploaded book (EPUB), on the server for every device.</summary>
public interface IBookmarkService
{
    /// <summary>The user's bookmarks in this book, in reading order.</summary>
    Task<IReadOnlyList<BookmarkInfo>> GetAsync(string userId, int resourceId);

    /// <summary>
    /// Adds a bookmark. Marking the same place again returns the existing bookmark. At most
    /// <see cref="BookmarkService.MaxPerBook"/> bookmarks per book.
    /// </summary>
    Task<ServiceResult<BookmarkInfo>> AddAsync(string userId, int resourceId, string? cfi, string? label, int percent);

    /// <summary>Removes one of the user's own bookmarks; another user's bookmark is "not found".</summary>
    Task<ServiceResult> RemoveAsync(string userId, int bookmarkId);
}

/// <summary>A saved bookmark.</summary>
public record BookmarkInfo(int Id, string Cfi, string Label, int Percent);
