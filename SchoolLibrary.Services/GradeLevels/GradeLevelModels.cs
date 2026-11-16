using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.GradeLevels;

/// <summary>A grade together with the number of resources filed under it.</summary>
public record GradeLevelListItem(GradeLevel GradeLevel, int ResourceCount);
