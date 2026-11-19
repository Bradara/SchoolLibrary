using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Manages the teacher's sections (topics) in the question bank.</summary>
public interface ISectionService
{
    /// <summary>
    /// One page of sections, by name. A null <paramref name="ownerId"/> returns every section (for an
    /// administrator); otherwise only that teacher's sections. Each item carries its question count.
    /// </summary>
    Task<PagedResult<SectionListItem>> GetManagedAsync(string? ownerId, int page, int pageSize = 10);

    /// <summary>The direct subsections of a section, by name, each with its counts.</summary>
    Task<IReadOnlyList<SectionListItem>> GetChildrenAsync(int parentId);

    /// <summary>A section by id, or null when it does not exist.</summary>
    Task<Section?> GetByIdAsync(int id);

    /// <summary>The ancestors of a section from the root down to its parent (empty for a root), for a breadcrumb.</summary>
    Task<IReadOnlyList<Section>> GetAncestorsAsync(int id);

    /// <summary>
    /// Creates a section owned by the teacher, optionally under a parent section. Fails on invalid
    /// input or when the parent does not exist.
    /// </summary>
    Task<ServiceResult<Section>> CreateAsync(SectionInput input, string ownerId, int? parentId = null);

    /// <summary>Changes a section's name and description. Fails when missing, not allowed, or invalid.</summary>
    Task<ServiceResult<Section>> UpdateAsync(int id, SectionInput input, string userId, bool isAdministrator);

    /// <summary>Deletes an empty section. Fails when missing, not allowed, or it still holds questions.</summary>
    Task<ServiceResult<Section>> DeleteAsync(int id, string userId, bool isAdministrator);
}
