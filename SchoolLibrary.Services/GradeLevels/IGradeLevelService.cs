using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.GradeLevels;

/// <summary>Reads the grades (5th to 12th) resources can be filed under.</summary>
public interface IGradeLevelService
{
    /// <summary>All grades ordered by number.</summary>
    Task<IReadOnlyList<GradeLevel>> GetAllAsync();
}
