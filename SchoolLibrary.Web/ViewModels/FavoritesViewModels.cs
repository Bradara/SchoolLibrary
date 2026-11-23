using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Reading;

namespace SchoolLibrary.ViewModels;

/// <summary>The start page: the newest resources the visitor may see.</summary>
public class HomeIndexViewModel
{
    public const int NewestCount = 6;

    public IReadOnlyList<Resource> Newest { get; set; } = new List<Resource>();

    /// <summary>Which of them the signed-in user saved (empty for a visitor).</summary>
    public IReadOnlySet<int> FavoriteIds { get; set; } = new HashSet<int>();

    /// <summary>All resources the visitor may see, for "Виж всички".</summary>
    public int TotalCount { get; set; }
}

public class FavoritesIndexViewModel
{
    public IReadOnlyList<Resource> Resources { get; set; } = new List<Resource>();

    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}

/// <summary>
/// What the heart button needs: which resource, whether it is saved, and where to go back to.
/// <paramref name="Compact"/> draws only the icon (on a catalog card) instead of a small button.
/// </summary>
public record FavoriteButtonModel(int ResourceId, bool IsFavorite, string ReturnUrl, bool Compact = false);

/// <summary>A resource with the favorite information shown on its details page.</summary>
public class ResourceDetailsViewModel
{
    public Resource Resource { get; set; } = null!;

    public bool IsFavorite { get; set; }

    /// <summary>How many users saved the resource.</summary>
    public int FavoriteCount { get; set; }

    /// <summary>Sanitized HTML of an uploaded Markdown file, shown inline; null for other resources.</summary>
    public string? MarkdownHtml { get; set; }

    // The comments under the resource (one page, newest first)
    public IReadOnlyList<Comment> Comments { get; set; } = new List<Comment>();
    public int CommentPage { get; set; } = 1;
    public int CommentTotalPages { get; set; }
    public int CommentTotal { get; set; }

    // Shown again after a comment was refused, so the user does not lose the text
    public string? CommentError { get; set; }
    public string? CommentDraft { get; set; }

    /// <summary>Where the signed-in user stopped reading this book (EPUB only); null otherwise.</summary>
    public ReadingPositionInfo? ReadingPosition { get; set; }

    /// <summary>Whether the current user may change who sees the resource (its owner, or an administrator for a resource without one).</summary>
    public bool CanChangeAudience { get; set; }
}
