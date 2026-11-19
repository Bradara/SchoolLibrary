using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Who may manage a section: its owner (the teacher who made it) or an administrator.</summary>
public static class SectionPermissions
{
    public static bool CanManage(Section section, string? userId, bool isAdministrator)
        => isAdministrator
           || (section.OwnerId != null && !string.IsNullOrEmpty(userId) && section.OwnerId == userId);
}
