using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Exams;

/// <summary>
/// Gives practice quizzes to classes as exams and lets the teacher run them: open or close the exam,
/// release the results to the students, and review the attempts.
/// </summary>
public interface IExamAssignmentService
{
    /// <summary>
    /// The class exams, newest first, with their attempt counts. A null owner returns every exam
    /// (administrator); a class id narrows the list to that class.
    /// </summary>
    Task<IReadOnlyList<ExamAssignmentListItem>> GetForOwnerAsync(string? ownerId, int? schoolClassId = null);

    /// <summary>The practice quizzes the owner can assign (with at least one question), by title. Null owner returns all.</summary>
    Task<IReadOnlyList<Quiz>> GetAssignableQuizzesAsync(string? ownerId);

    /// <summary>A class exam with its quiz and class, or null when it does not exist.</summary>
    Task<ExamAssignment?> GetByIdAsync(int id);

    /// <summary>
    /// Gives a practice quiz to a class as an exam with a new unique code. It starts open, with the
    /// results hidden from the students. Fails when the quiz or class is missing or not allowed, the
    /// quiz is not a practice quiz or has no questions, or the settings are out of range.
    /// </summary>
    Task<ServiceResult<ExamAssignment>> CreateAsync(ExamAssignmentInput input, string userId, bool isAdministrator);

    /// <summary>Changes the time limit and proctoring. Fails when missing, not allowed, or out of range.</summary>
    Task<ServiceResult<ExamAssignment>> UpdateSettingsAsync(int id, ExamSettingsInput input, string userId, bool isAdministrator);

    /// <summary>Opens or closes the exam for new attempts. Fails when missing or not allowed.</summary>
    Task<ServiceResult<ExamAssignment>> SetOpenAsync(int id, bool open, string userId, bool isAdministrator);

    /// <summary>Shows or hides the results to the students. Fails when missing or not allowed.</summary>
    Task<ServiceResult<ExamAssignment>> SetResultsReleasedAsync(int id, bool released, string userId, bool isAdministrator);

    /// <summary>Deletes the exam with its attempts. Fails when missing or not allowed.</summary>
    Task<ServiceResult<ExamAssignment>> DeleteAsync(int id, string userId, bool isAdministrator);

    /// <summary>
    /// The exam with one page of its attempts (submitted and in progress), by class number. Fails when
    /// missing or not allowed.
    /// </summary>
    Task<ServiceResult<ExamResults>> GetResultsAsync(int id, string userId, bool isAdministrator, int page, int pageSize = 50);

    /// <summary>
    /// One submitted attempt at a class exam, graded, with the options in the order the student saw.
    /// Fails when missing, not a class exam attempt, not submitted yet, or not allowed.
    /// </summary>
    Task<ServiceResult<ExamAttemptReview>> GetAttemptReviewAsync(int attemptId, string userId, bool isAdministrator);
}
