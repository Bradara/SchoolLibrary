using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Comments;

public class CommentService : ICommentService
{
    public const int TextMaxLength = 500;
    public const int MaxPageSize = 100;

    private readonly ApplicationDbContext _context;

    public CommentService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<Comment>> GetForResourceAsync(int resourceId, int page, int pageSize = 10)
    {
        var query = _context.Comments.AsNoTracking().Where(c => c.ResourceId == resourceId);

        var totalCount = await query.CountAsync();

        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        page = Math.Clamp(page, 1, Math.Max(totalPages, 1));

        // Id as the last key keeps the order stable when two comments have the same time
        var items = await query
            .OrderByDescending(c => c.CreatedOn)
            .ThenByDescending(c => c.Id)
            .Include(c => c.Author)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Comment>(items, totalCount, page, pageSize);
    }

    public async Task<ServiceResult<Comment>> AddAsync(int resourceId, string authorId, string text)
    {
        // A browser counts a line break as one character but sends two (CRLF), so the length is
        // checked on the text with single line breaks: what the user saw is what is checked
        text = (text ?? string.Empty).Replace("\r\n", "\n").Trim();

        if (text.Length == 0)
        {
            return ServiceResult<Comment>.Failure(ServiceError.Invalid, "Напишете коментар.");
        }

        if (text.Length > TextMaxLength)
        {
            return ServiceResult<Comment>.Failure(ServiceError.Invalid,
                $"Коментарът не може да бъде по-дълъг от {TextMaxLength} символа.");
        }

        if (!await _context.Resources.AnyAsync(r => r.Id == resourceId))
        {
            return ServiceResult<Comment>.Failure(ServiceError.NotFound, "Ресурсът не е намерен.");
        }

        var comment = new Comment
        {
            ResourceId = resourceId,
            AuthorId = authorId,
            Text = text,
            CreatedOn = DateTime.UtcNow
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        return ServiceResult<Comment>.Success(comment);
    }

    public async Task<ServiceResult<Comment>> DeleteAsync(int commentId, string userId, bool isAdministrator)
    {
        var comment = await _context.Comments.FirstOrDefaultAsync(c => c.Id == commentId);
        if (comment == null)
        {
            return ServiceResult<Comment>.Failure(ServiceError.NotFound, "Коментарът не е намерен.");
        }

        if (!CommentPermissions.CanDelete(comment, userId, isAdministrator))
        {
            return ServiceResult<Comment>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        _context.Comments.Remove(comment);
        await _context.SaveChangesAsync();

        return ServiceResult<Comment>.Success(comment);
    }
}
