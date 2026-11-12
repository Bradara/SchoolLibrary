using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Categories;

/// <summary>Manages the subjects (categories) resources are filed under.</summary>
public interface ICategoryService
{
    /// <summary>All subjects ordered by name, each with its resources loaded.</summary>
    Task<IReadOnlyList<Category>> GetAllAsync();

    /// <summary>A subject without its resources, or null when it does not exist.</summary>
    Task<Category?> GetByIdAsync(int id);

    /// <summary>A subject with its resources loaded, or null when it does not exist.</summary>
    Task<Category?> GetWithResourcesAsync(int id);

    /// <summary>Creates a subject. Fails when the name is empty, too long or already used.</summary>
    Task<ServiceResult<Category>> CreateAsync(string name);

    /// <summary>Renames a subject. Fails when it does not exist or the new name is invalid or used.</summary>
    Task<ServiceResult<Category>> UpdateAsync(int id, string name);

    /// <summary>Deletes a subject. Fails when it does not exist or still has resources.</summary>
    Task<ServiceResult<Category>> DeleteAsync(int id);
}
