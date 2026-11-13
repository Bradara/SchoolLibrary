using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Common;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Resources;

public class ResourceService : IResourceService
{
    public const int TitleMaxLength = 100;
    public const int AuthorMaxLength = 100;
    public const int SourceMaxLength = 100;
    public const int DescriptionMaxLength = 1000;
    public const int UrlMaxLength = 500;

    private const string NotFoundMessage = "Ресурсът не е намерен.";
    private const string DuplicateMessage = "Този материал вече съществува в библиотеката.";
    private const string ForbiddenMessage = "Нямате права за това действие.";

    private readonly ApplicationDbContext _context;

    public ResourceService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<Resource>> SearchAsync(ResourceFilter filter)
    {
        var query = ApplyFilter(_context.Resources.AsNoTracking(), filter);

        var totalCount = await query.CountAsync();

        var pageSize = Math.Clamp(filter.PageSize, 1, ResourceFilter.MaxPageSize);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var page = Math.Clamp(filter.Page, 1, Math.Max(totalPages, 1));

        var items = await ApplySort(query, filter.SortOrder)
            .Include(r => r.Categories)
            .Include(r => r.GradeLevels)
            .Include(r => r.Owner)
            .Include(r => r.License)
            .AsSplitQuery()
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Resource>(items, totalCount, page, pageSize);
    }

    public async Task<IReadOnlyList<ResourceOwnerOption>> GetOwnersAsync()
    {
        return await _context.Users
            .AsNoTracking()
            .Where(u => _context.Resources.Any(r => r.OwnerId == u.Id))
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .Select(u => new ResourceOwnerOption(u.Id, u.FirstName + " " + u.LastName))
            .ToListAsync();
    }

    public async Task<Resource?> GetDetailsAsync(int id)
    {
        return await _context.Resources
            .AsNoTracking()
            .Include(r => r.Categories)
            .Include(r => r.GradeLevels)
            .Include(r => r.Owner)
            .Include(r => r.License)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<Resource?> FindByUrlAsync(string url)
    {
        var normalized = UrlNormalizer.Normalize(url);
        if (normalized == null)
        {
            return null;
        }

        return await _context.Resources
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(r => r.NormalizedUrl == normalized);
    }

    public async Task<ServiceResult<Resource>> GetForEditAsync(int id, string userId)
    {
        var resource = await GetDetailsAsync(id);
        if (resource == null)
        {
            return ServiceResult<Resource>.Failure(ServiceError.NotFound, NotFoundMessage);
        }

        // Administrators may delete other people's resources but not edit them
        if (resource.OwnerId != userId)
        {
            return ServiceResult<Resource>.Failure(ServiceError.Forbidden, ForbiddenMessage);
        }

        return ServiceResult<Resource>.Success(resource);
    }

    public async Task<ServiceResult<Resource>> GetForDeleteAsync(int id, string userId, bool isAdministrator)
    {
        var resource = await GetDetailsAsync(id);
        if (resource == null)
        {
            return ServiceResult<Resource>.Failure(ServiceError.NotFound, NotFoundMessage);
        }

        if (!CanDelete(resource, userId, isAdministrator))
        {
            return ServiceResult<Resource>.Failure(ServiceError.Forbidden, ForbiddenMessage);
        }

        return ServiceResult<Resource>.Success(resource);
    }

    public async Task<ServiceResult<Resource>> CreateAsync(ResourceInput input, string? ownerId)
    {
        var validation = Validate(input);
        if (validation != null)
        {
            return ServiceResult<Resource>.Failure(ServiceError.Invalid, validation);
        }

        if (await IsDuplicateAsync(input.Url, excludeId: null))
        {
            return ServiceResult<Resource>.Failure(ServiceError.Conflict, DuplicateMessage);
        }

        var resource = new Resource
        {
            OwnerId = ownerId,
            CreatedOn = DateTime.UtcNow
        };

        var error = await ApplyInputAsync(resource, input);
        if (error != null)
        {
            return ServiceResult<Resource>.Failure(ServiceError.Invalid, error);
        }

        _context.Resources.Add(resource);
        await _context.SaveChangesAsync();

        return ServiceResult<Resource>.Success(resource);
    }

    public async Task<ServiceResult<Resource>> UpdateAsync(int id, ResourceInput input, string userId)
    {
        var resource = await _context.Resources
            .Include(r => r.Categories)
            .Include(r => r.GradeLevels)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (resource == null)
        {
            return ServiceResult<Resource>.Failure(ServiceError.NotFound, NotFoundMessage);
        }

        if (resource.OwnerId != userId)
        {
            return ServiceResult<Resource>.Failure(ServiceError.Forbidden, ForbiddenMessage);
        }

        var validation = Validate(input);
        if (validation != null)
        {
            return ServiceResult<Resource>.Failure(ServiceError.Invalid, validation);
        }

        if (await IsDuplicateAsync(input.Url, excludeId: id))
        {
            return ServiceResult<Resource>.Failure(ServiceError.Conflict, DuplicateMessage);
        }

        var error = await ApplyInputAsync(resource, input);
        if (error != null)
        {
            return ServiceResult<Resource>.Failure(ServiceError.Invalid, error);
        }

        await _context.SaveChangesAsync();

        return ServiceResult<Resource>.Success(resource);
    }

    public async Task<ServiceResult<Resource>> DeleteAsync(int id, string userId, bool isAdministrator)
    {
        var resource = await _context.Resources.FirstOrDefaultAsync(r => r.Id == id);
        if (resource == null)
        {
            return ServiceResult<Resource>.Failure(ServiceError.NotFound, NotFoundMessage);
        }

        if (!CanDelete(resource, userId, isAdministrator))
        {
            return ServiceResult<Resource>.Failure(ServiceError.Forbidden, ForbiddenMessage);
        }

        _context.Resources.Remove(resource);
        await _context.SaveChangesAsync();

        return ServiceResult<Resource>.Success(resource);
    }

    private async Task<bool> IsDuplicateAsync(string url, int? excludeId)
    {
        var normalized = UrlNormalizer.Normalize(url);
        return normalized != null
            && await _context.Resources.AnyAsync(r => r.NormalizedUrl == normalized && r.Id != excludeId);
    }

    private static bool CanDelete(Resource resource, string userId, bool isAdministrator) =>
        isAdministrator || resource.OwnerId == userId;

    private static IQueryable<Resource> ApplyFilter(IQueryable<Resource> query, ResourceFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            // Lower-casing both sides makes the search case-insensitive in PostgreSQL as well
            var term = filter.SearchText.Trim().ToLower();
            query = query.Where(r => r.Title.ToLower().Contains(term) || r.Author.ToLower().Contains(term));
        }

        if (filter.Kind.HasValue)
        {
            var kind = filter.Kind.Value;
            query = query.Where(r => r.Kind == kind);
        }

        if (filter.CategoryId.HasValue)
        {
            var categoryId = filter.CategoryId.Value;
            query = query.Where(r => r.Categories.Any(c => c.Id == categoryId));
        }

        if (filter.GradeLevelId.HasValue)
        {
            var gradeLevelId = filter.GradeLevelId.Value;
            query = query.Where(r => r.GradeLevels.Any(g => g.Id == gradeLevelId));
        }

        if (!string.IsNullOrEmpty(filter.OwnerId))
        {
            var ownerId = filter.OwnerId;
            query = query.Where(r => r.OwnerId == ownerId);
        }

        if (filter.FromDate.HasValue)
        {
            // PostgreSQL timestamptz only accepts UTC values
            var from = DateTime.SpecifyKind(filter.FromDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(r => r.CreatedOn >= from);
        }

        if (filter.ToDate.HasValue)
        {
            // The whole last day is included
            var endExclusive = DateTime.SpecifyKind(filter.ToDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(r => r.CreatedOn < endExclusive);
        }

        return query;
    }

    // Id as the last key keeps the order stable, so paging never repeats or skips a resource
    private static IQueryable<Resource> ApplySort(IQueryable<Resource> query, ResourceSortOrder sortOrder) =>
        sortOrder switch
        {
            ResourceSortOrder.DateAsc => query.OrderBy(r => r.CreatedOn).ThenBy(r => r.Id),
            ResourceSortOrder.TitleAsc => query.OrderBy(r => r.Title.ToLower()).ThenByDescending(r => r.CreatedOn).ThenBy(r => r.Id),
            ResourceSortOrder.TitleDesc => query.OrderByDescending(r => r.Title.ToLower()).ThenByDescending(r => r.CreatedOn).ThenBy(r => r.Id),
            ResourceSortOrder.AuthorAsc => query.OrderBy(r => r.Author.ToLower()).ThenByDescending(r => r.CreatedOn).ThenBy(r => r.Id),
            ResourceSortOrder.AuthorDesc => query.OrderByDescending(r => r.Author.ToLower()).ThenByDescending(r => r.CreatedOn).ThenBy(r => r.Id),
            ResourceSortOrder.OwnerAsc => query.OrderBy(r => r.Owner!.FirstName).ThenBy(r => r.Owner!.LastName).ThenByDescending(r => r.CreatedOn).ThenBy(r => r.Id),
            _ => query.OrderByDescending(r => r.CreatedOn).ThenBy(r => r.Id)
        };

    // Also used by the CSV import, so a file row must pass the same rules as the form
    internal static string? Validate(ResourceInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
        {
            return "Заглавието е задължително.";
        }

        if (input.Title.Trim().Length > TitleMaxLength)
        {
            return $"Заглавието не може да бъде по-дълго от {TitleMaxLength} символа.";
        }

        if (string.IsNullOrWhiteSpace(input.Author))
        {
            return "Посочете автора на материала.";
        }

        if (input.Author.Trim().Length > AuthorMaxLength)
        {
            return $"Авторът не може да бъде по-дълъг от {AuthorMaxLength} символа.";
        }

        if (input.Source != null && input.Source.Trim().Length > SourceMaxLength)
        {
            return $"Източникът не може да бъде по-дълъг от {SourceMaxLength} символа.";
        }

        if (input.Description != null && input.Description.Trim().Length > DescriptionMaxLength)
        {
            return $"Описанието не може да бъде по-дълго от {DescriptionMaxLength} символа.";
        }

        if (string.IsNullOrWhiteSpace(input.Url))
        {
            return "Уеб адресът е задължителен.";
        }

        if (input.Url.Trim().Length > UrlMaxLength)
        {
            return $"Уеб адресът не може да бъде по-дълъг от {UrlMaxLength} символа.";
        }

        // Only http(s) links are accepted: a "javascript:" link in an href would run script in the visitor's browser
        if (!Uri.TryCreate(input.Url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "Уеб адресът трябва да започва с http:// или https://.";
        }

        if (input.LicenseId == null)
        {
            return "Изберете лиценз.";
        }

        if (input.CategoryIds.Count == 0)
        {
            return "Изберете поне един предмет.";
        }

        if (input.GradeLevelIds.Count == 0)
        {
            return "Изберете поне един клас.";
        }

        return null;
    }

    // Copies the validated input to the resource and loads the chosen subjects and grades
    private async Task<string?> ApplyInputAsync(Resource resource, ResourceInput input)
    {
        var categoryIds = input.CategoryIds.Distinct().ToList();
        var categories = await _context.Categories.Where(c => categoryIds.Contains(c.Id)).ToListAsync();
        if (categories.Count != categoryIds.Count)
        {
            return "Избран е несъществуващ предмет.";
        }

        var gradeLevelIds = input.GradeLevelIds.Distinct().ToList();
        var gradeLevels = await _context.GradeLevels.Where(g => gradeLevelIds.Contains(g.Id)).ToListAsync();
        if (gradeLevels.Count != gradeLevelIds.Count)
        {
            return "Избран е несъществуващ клас.";
        }

        var license = await _context.Licenses.FirstOrDefaultAsync(l => l.Id == input.LicenseId);
        if (license == null)
        {
            return "Избран е несъществуващ лиценз.";
        }

        resource.Title = input.Title.Trim();
        resource.Author = input.Author.Trim();
        resource.Source = string.IsNullOrWhiteSpace(input.Source) ? null : input.Source.Trim();
        resource.Description = input.Description?.Trim() ?? string.Empty;
        resource.Url = input.Url.Trim();
        resource.NormalizedUrl = UrlNormalizer.Normalize(resource.Url);
        resource.License = license;
        resource.Kind = input.Kind;

        resource.Categories.Clear();
        foreach (var category in categories)
        {
            resource.Categories.Add(category);
        }

        resource.GradeLevels.Clear();
        foreach (var gradeLevel in gradeLevels)
        {
            resource.GradeLevels.Add(gradeLevel);
        }

        return null;
    }
}
