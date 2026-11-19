using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Creates and manages quizzes and their questions (the teacher-facing authoring side).</summary>
public interface IQuizService
{
    /// <summary>A quiz with its questions and options (ordered), or null when it does not exist.</summary>
    Task<Quiz?> GetByIdAsync(int id);

    /// <summary>
    /// One page of quizzes for management, newest first. A null <paramref name="ownerId"/> returns every
    /// quiz (for an administrator); otherwise only that teacher's quizzes.
    /// </summary>
    Task<PagedResult<QuizListItem>> GetManagedAsync(string? ownerId, int page, int pageSize = 10);

    /// <summary>Creates a quiz owned by the teacher. An exam gets a unique code. Fails on invalid input.</summary>
    Task<ServiceResult<Quiz>> CreateAsync(QuizInput input, string ownerId);

    /// <summary>Changes a quiz's details. Fails when missing, not allowed, invalid, or the mode change is blocked by existing attempts.</summary>
    Task<ServiceResult<Quiz>> UpdateAsync(int id, QuizInput input, string userId, bool isAdministrator);

    /// <summary>Deletes a quiz with its questions and attempts. Fails when missing or not allowed.</summary>
    Task<ServiceResult<Quiz>> DeleteAsync(int id, string userId, bool isAdministrator);

    /// <summary>Publishes or unpublishes a quiz. Publishing needs at least one question. Fails when missing or not allowed.</summary>
    Task<ServiceResult<Quiz>> SetPublishedAsync(int id, bool published, string userId, bool isAdministrator);

    /// <summary>Adds a question with its options. Fails when the quiz is missing, not allowed, already has attempts, or the input is invalid.</summary>
    Task<ServiceResult<Question>> AddQuestionAsync(int quizId, QuestionInput input, string userId, bool isAdministrator);

    /// <summary>Removes a question. Fails when it is missing, not allowed, or the quiz already has attempts.</summary>
    Task<ServiceResult<Question>> DeleteQuestionAsync(int questionId, string userId, bool isAdministrator);

    /// <summary>One page of published practice quizzes to browse, each with its question count.</summary>
    Task<PagedResult<QuizListItem>> GetPublishedPracticeAsync(int page, int pageSize = 10);

    /// <summary>A published practice quiz with its questions and options for solving, or null otherwise.</summary>
    Task<Quiz?> GetPracticeForSolvingAsync(int id);

    /// <summary>Grades answers to a published practice quiz. Fails when the quiz is missing, an exam, or unpublished.</summary>
    Task<ServiceResult<QuizResult>> GradePracticeAsync(int quizId, IReadOnlyDictionary<int, int?> answers);

    /// <summary>
    /// Starts (or resumes) an exam entered by its code. A student who already submitted this exam is
    /// blocked; an unfinished attempt for the same name and number is resumed. Fails when the code is
    /// unknown or the name is missing.
    /// </summary>
    Task<ServiceResult<QuizAttempt>> StartExamAsync(string code, string studentName, string? studentNumber);

    /// <summary>An exam attempt with its quiz, questions and options, found by its token, or null.</summary>
    Task<QuizAttempt?> GetExamAttemptAsync(Guid token);

    /// <summary>
    /// Grades and stores an exam attempt. Resubmitting an already-submitted attempt returns the stored
    /// result unchanged. Fails when the token is unknown.
    /// </summary>
    Task<ServiceResult<QuizResult>> SubmitExamAsync(Guid token, IReadOnlyDictionary<int, int?> answers);

    /// <summary>The graded result of a submitted exam attempt. Fails when the token is unknown or not yet submitted.</summary>
    Task<ServiceResult<QuizResult>> GetExamResultAsync(Guid token);

    /// <summary>
    /// A quiz with one page of its submitted attempts, for the owner (or an administrator) to review.
    /// Fails when the quiz is missing or the requester may not manage it.
    /// </summary>
    Task<ServiceResult<QuizReview>> GetReviewAsync(int quizId, string userId, bool isAdministrator, int page, int pageSize = 20);
}
