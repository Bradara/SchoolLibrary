using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Categories;

/// <summary>Manages the subjects (categories) resources are filed under.</summary>
public interface ICategoryService
{
    /// <summary>All subjects ordered by name, each with its resources loaded.</summary>
    Task<IReadOnlyList<Category>> GetAllAsync();

    /// <summary>All subjects ordered by name, without their resources (for drop-downs and checkbox lists).</summary>
    Task<IReadOnlyList<Category>> GetOptionsAsync();

    /// <summary>A subject without its resources, or null when it does not exist.</summary>
    Task<Category?> GetByIdAsync(int id);

    /// <summary>A subject with its resources loaded, or null when it does not exist.</summary>
    Task<Category?> GetWithResourcesAsync(int id);

    /// <summary>
    /// Creates a subject. Fails when the name is empty, too long or already used, or the icon or the color
    /// is not from the lists. Without a chosen icon or color the subject gets the look for its name.
    /// </summary>
    Task<ServiceResult<Category>> CreateAsync(CategoryInput input);

    /// <summary>
    /// Changes the name and the look of a subject. Fails when it does not exist, the new name is invalid or used,
    /// or the icon or the color is not from the lists. Without a chosen icon or color it keeps the current one.
    /// </summary>
    Task<ServiceResult<Category>> UpdateAsync(int id, CategoryInput input);

    /// <summary>Deletes a subject. Fails when it does not exist or still has resources.</summary>
    Task<ServiceResult<Category>> DeleteAsync(int id);
}
