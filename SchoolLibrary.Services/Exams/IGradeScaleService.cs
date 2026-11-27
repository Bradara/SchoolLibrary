using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Exams;

/// <summary>Manages a teacher's own grade scales (the thresholds for the 2–6 grades) that exams point at.</summary>
public interface IGradeScaleService
{
    /// <summary>The owner's scales by name, each with the number of exams using it. A null owner returns every scale (administrator).</summary>
    Task<IReadOnlyList<GradeScaleListItem>> GetForOwnerAsync(string? ownerId);

    /// <summary>A scale the user may manage; NotFound or Forbidden otherwise.</summary>
    Task<ServiceResult<GradeScale>> GetAsync(int id, string userId, bool isAdministrator);

    /// <summary>Creates a scale for the teacher. Fails when the name is empty, too long or taken, or the thresholds are invalid.</summary>
    Task<ServiceResult<GradeScale>> CreateAsync(GradeScaleInput input, string ownerId);

    /// <summary>
    /// Changes a scale's name and thresholds. The grades of every exam using it change too (the exams point at it).
    /// </summary>
    Task<ServiceResult<GradeScale>> UpdateAsync(int id, GradeScaleInput input, string userId, bool isAdministrator);

    /// <summary>Deletes a scale. Fails (Conflict) while an exam uses it.</summary>
    Task<ServiceResult<GradeScale>> DeleteAsync(int id, string userId, bool isAdministrator);
}
