using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Favorites;

public class FavoriteService : IFavoriteService
{
    public const int MaxPageSize = 100;

    private readonly ApplicationDbContext _context;

    public FavoriteService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult> AddAsync(string userId, int resourceId)
    {
        if (!await _context.Resources.AnyAsync(r => r.Id == resourceId))
        {
            return ServiceResult.Failure(ServiceError.NotFound, "Ресурсът не е намерен.");
        }

        var exists = await _context.Favorites.AnyAsync(f => f.UserId == userId && f.ResourceId == resourceId);
        if (!exists)
        {
            _context.Favorites.Add(new Favorite
            {
                UserId = userId,
                ResourceId = resourceId,
                CreatedOn = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RemoveAsync(string userId, int resourceId)
    {
        var favorite = await _context.Favorites
            .FirstOrDefaultAsync(f => f.UserId == userId && f.ResourceId == resourceId);

        if (favorite != null)
        {
            _context.Favorites.Remove(favorite);
            await _context.SaveChangesAsync();
        }

        return ServiceResult.Success();
    }

    public async Task<HashSet<int>> GetFavoriteIdsAsync(string userId, IEnumerable<int> resourceIds)
    {
        var ids = resourceIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new HashSet<int>();
        }

        var saved = await _context.Favorites
            .AsNoTracking()
            .Where(f => f.UserId == userId && ids.Contains(f.ResourceId))
            .Select(f => f.ResourceId)
            .ToListAsync();

        return saved.ToHashSet();
    }

    public Task<bool> IsFavoriteAsync(string userId, int resourceId) =>
        _context.Favorites.AnyAsync(f => f.UserId == userId && f.ResourceId == resourceId);

    public Task<int> CountAsync(int resourceId) =>
        _context.Favorites.CountAsync(f => f.ResourceId == resourceId);

    public async Task<PagedResult<Resource>> GetFavoritesAsync(string userId, int page, int pageSize = 10)
    {
        var mine = _context.Favorites.AsNoTracking().Where(f => f.UserId == userId);

        var totalCount = await mine.CountAsync();

        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        page = Math.Clamp(page, 1, Math.Max(totalPages, 1));

        // First the ids of this page in saving order, then the resources: the order is kept in memory
        var pageIds = await mine
            .OrderByDescending(f => f.CreatedOn)
            .ThenByDescending(f => f.ResourceId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => f.ResourceId)
            .ToListAsync();

        var resources = await _context.Resources
            .AsNoTracking()
            .Where(r => pageIds.Contains(r.Id))
            .Include(r => r.Categories)
            .Include(r => r.GradeLevels)
            .Include(r => r.Owner)
            .Include(r => r.License)
            .AsSplitQuery()
            .ToListAsync();

        var items = pageIds
            .Select(id => resources.First(r => r.Id == id))
            .ToList();

        return new PagedResult<Resource>(items, totalCount, page, pageSize);
    }
}
