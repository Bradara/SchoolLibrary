using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Who may manage a quiz: its owner (the teacher who made it) or an administrator.</summary>
public static class QuizPermissions
{
    public static bool CanManage(Quiz quiz, string? userId, bool isAdministrator)
        => isAdministrator
           || (quiz.OwnerId != null && !string.IsNullOrEmpty(userId) && quiz.OwnerId == userId);
}
