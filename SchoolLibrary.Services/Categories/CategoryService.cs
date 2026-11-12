using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Categories;

public class CategoryService : ICategoryService
{
    public const int MaxNameLength = 50;

    private readonly ApplicationDbContext _context;

    public CategoryService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Category>> GetAllAsync()
    {
        return await _context.Categories
            .Include(c => c.Resources)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Category>> GetOptionsAsync()
    {
        return await _context.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<Category?> GetByIdAsync(int id)
    {
        return await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Category?> GetWithResourcesAsync(int id)
    {
        return await _context.Categories
            .Include(c => c.Resources)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<ServiceResult<Category>> CreateAsync(string name)
    {
        var validation = ValidateName(name);
        if (validation != null)
        {
            return ServiceResult<Category>.Failure(ServiceError.Invalid, validation);
        }

        name = name.Trim();
        if (await NameExistsAsync(name, excludeId: null))
        {
            return ServiceResult<Category>.Failure(ServiceError.Conflict, $"Предмет с име {name} вече съществува.");
        }

        var category = new Category { Name = name };
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        return ServiceResult<Category>.Success(category);
    }

    public async Task<ServiceResult<Category>> UpdateAsync(int id, string name)
    {
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return ServiceResult<Category>.Failure(ServiceError.NotFound, "Предметът не е намерен.");
        }

        var validation = ValidateName(name);
        if (validation != null)
        {
            return ServiceResult<Category>.Failure(ServiceError.Invalid, validation);
        }

        name = name.Trim();
        if (await NameExistsAsync(name, excludeId: id))
        {
            return ServiceResult<Category>.Failure(ServiceError.Conflict, $"Предмет с име {name} вече съществува.");
        }

        category.Name = name;
        await _context.SaveChangesAsync();

        return ServiceResult<Category>.Success(category);
    }

    public async Task<ServiceResult<Category>> DeleteAsync(int id)
    {
        var category = await GetWithResourcesAsync(id);
        if (category == null)
        {
            return ServiceResult<Category>.Failure(ServiceError.NotFound, "Предметът не е намерен.");
        }

        if (category.Resources.Count > 0)
        {
            return ServiceResult<Category>.Failure(
                ServiceError.Conflict,
                $"Предметът {category.Name} не може да бъде изтрит — има {category.Resources.Count} свързани ресурса.");
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        return ServiceResult<Category>.Success(category);
    }

    private static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Името е задължително.";
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return $"Името не може да бъде по-дълго от {MaxNameLength} символа.";
        }

        return null;
    }

    // Names are compared ignoring case, so two spellings of the same subject count as one
    private Task<bool> NameExistsAsync(string name, int? excludeId)
    {
        var lowered = name.ToLower();
        return _context.Categories.AnyAsync(c => c.Name.ToLower() == lowered && c.Id != excludeId);
    }
}
