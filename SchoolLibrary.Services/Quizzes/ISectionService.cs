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

    /// <summary>A section by id, or null when it does not exist.</summary>
    Task<Section?> GetByIdAsync(int id);

    /// <summary>Creates a section owned by the teacher. Fails on invalid input.</summary>
    Task<ServiceResult<Section>> CreateAsync(SectionInput input, string ownerId);

    /// <summary>Changes a section's name and description. Fails when missing, not allowed, or invalid.</summary>
    Task<ServiceResult<Section>> UpdateAsync(int id, SectionInput input, string userId, bool isAdministrator);

    /// <summary>Deletes an empty section. Fails when missing, not allowed, or it still holds questions.</summary>
    Task<ServiceResult<Section>> DeleteAsync(int id, string userId, bool isAdministrator);
}
