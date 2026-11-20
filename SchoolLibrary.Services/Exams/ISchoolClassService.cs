using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Exams;

/// <summary>Manages the teacher's classes (e.g. "5А") that exams are assigned to.</summary>
public interface ISchoolClassService
{
    /// <summary>The owner's classes by name, each with its exam count. A null owner returns every class (administrator).</summary>
    Task<IReadOnlyList<SchoolClassListItem>> GetForOwnerAsync(string? ownerId);

    /// <summary>A class by id, or null when it does not exist.</summary>
    Task<SchoolClass?> GetByIdAsync(int id);

    /// <summary>Creates a class for the teacher. Fails when the name is empty, too long, or already used by the teacher.</summary>
    Task<ServiceResult<SchoolClass>> CreateAsync(string name, string ownerId);

    /// <summary>Renames a class. Fails when missing, not allowed, or the name is invalid or taken.</summary>
    Task<ServiceResult<SchoolClass>> RenameAsync(int id, string name, string userId, bool isAdministrator);

    /// <summary>Deletes a class with its exams and their attempts. Fails when missing or not allowed.</summary>
    Task<ServiceResult<SchoolClass>> DeleteAsync(int id, string userId, bool isAdministrator);
}
