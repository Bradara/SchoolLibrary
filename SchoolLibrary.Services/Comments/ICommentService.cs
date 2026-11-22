using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Services.Comments;

/// <summary>Comments under resources: reading, adding and deleting.</summary>
public interface ICommentService
{
    /// <summary>One page of the comments under a resource, newest first, each with its author.</summary>
    Task<PagedResult<Comment>> GetForResourceAsync(int resourceId, int page, int pageSize = 10);

    /// <summary>A comment with its author and the resource it is under, or null when it does not exist.</summary>
    Task<Comment?> GetAsync(int commentId);

    /// <summary>
    /// Adds a comment. Fails when the text is empty or longer than 500 characters,
    /// or when the resource does not exist or the author may not see it.
    /// </summary>
    Task<ServiceResult<Comment>> AddAsync(int resourceId, ResourceViewer author, string text);

    /// <summary>Deletes a comment. Its author or an administrator may delete it.</summary>
    Task<ServiceResult<Comment>> DeleteAsync(int commentId, string userId, bool isAdministrator);
}
