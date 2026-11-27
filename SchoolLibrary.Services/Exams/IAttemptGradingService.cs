using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Exams;

/// <summary>The teacher's own points and note on an answer of a submitted attempt.</summary>
public interface IAttemptGradingService
{
    /// <summary>
    /// Sets the points of one question in a submitted attempt and the note to the student, then updates the
    /// attempt's score. <paramref name="points"/> null brings back the automatic points; an empty
    /// <paramref name="comment"/> removes the note. A skipped question can be given points too.
    /// </summary>
    /// <returns>The attempt with its new score; Invalid when the points are out of range, the note is too long,
    /// the attempt is not submitted or the program is still being checked.</returns>
    Task<ServiceResult<QuizAttempt>> SetPointsAsync(int attemptId, int questionId, decimal? points, string? comment,
        string userId, bool isAdministrator);
}
