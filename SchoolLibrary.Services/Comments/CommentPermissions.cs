using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Comments;

/// <summary>Who may delete a comment. Used by the service and by the view that shows the delete button.</summary>
public static class CommentPermissions
{
    /// <summary>The author or an administrator. The owner of the resource may not.</summary>
    public static bool CanDelete(Comment comment, string? userId, bool isAdministrator) =>
        isAdministrator || (userId != null && comment.AuthorId == userId);
}
