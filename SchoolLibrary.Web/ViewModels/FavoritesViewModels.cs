using SchoolLibrary.Data.Models;

namespace SchoolLibrary.ViewModels;

public class FavoritesIndexViewModel
{
    public IReadOnlyList<Resource> Resources { get; set; } = new List<Resource>();

    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}

/// <summary>What the heart button needs: which resource, whether it is saved, and where to go back to.</summary>
public record FavoriteButtonModel(int ResourceId, bool IsFavorite, string ReturnUrl);

/// <summary>A resource with the favorite information shown on its details page.</summary>
public class ResourceDetailsViewModel
{
    public Resource Resource { get; set; } = null!;

    public bool IsFavorite { get; set; }

    /// <summary>How many users saved the resource.</summary>
    public int FavoriteCount { get; set; }

    // The comments under the resource (one page, newest first)
    public IReadOnlyList<Comment> Comments { get; set; } = new List<Comment>();
    public int CommentPage { get; set; } = 1;
    public int CommentTotalPages { get; set; }
    public int CommentTotal { get; set; }

    // Shown again after a comment was refused, so the user does not lose the text
    public string? CommentError { get; set; }
    public string? CommentDraft { get; set; }
}
