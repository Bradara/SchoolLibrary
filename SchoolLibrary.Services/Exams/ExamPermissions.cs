using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Exams;

/// <summary>Who may manage a class and its exams: the teacher who made the class, or an administrator.</summary>
public static class ExamPermissions
{
    public static bool CanManage(SchoolClass schoolClass, string? userId, bool isAdministrator)
        => isAdministrator
           || (schoolClass.OwnerId != null && !string.IsNullOrEmpty(userId) && schoolClass.OwnerId == userId);
}
