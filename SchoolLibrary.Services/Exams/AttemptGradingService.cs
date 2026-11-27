using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Services.Exams;

public class AttemptGradingService : IAttemptGradingService
{
    private readonly ApplicationDbContext _context;

    public AttemptGradingService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<QuizAttempt>> SetPointsAsync(int attemptId, int questionId, decimal? points, string? comment,
        string userId, bool isAdministrator)
    {
        var attempt = await _context.QuizAttempts
            .Include(a => a.ExamAssignment!)
                .ThenInclude(e => e.SchoolClass)
            .Include(a => a.Quiz)
                .ThenInclude(q => q.Questions)
                    .ThenInclude(q => q.Options)
            .Include(a => a.Questions)
                .ThenInclude(aq => aq.Question)
                    .ThenInclude(q => q.Options)
            .Include(a => a.Answers)
                .ThenInclude(answer => answer.Choices)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == attemptId);
        if (attempt == null)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.NotFound, "Опитът не е намерен.");
        }

        // A class exam belongs to the teacher of the class, an older exam to the teacher of the quiz
        var canManage = attempt.ExamAssignment != null
            ? ExamPermissions.CanManage(attempt.ExamAssignment.SchoolClass, userId, isAdministrator)
            : QuizPermissions.CanManage(attempt.Quiz, userId, isAdministrator);
        if (!canManage)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        if (attempt.SubmittedOn == null)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Invalid, "Опитът още не е предаден.");
        }

        var question = QuizAttemptResults.SnapshotQuestions(attempt).FirstOrDefault(q => q.Id == questionId);
        if (question == null || question.Type == QuestionType.Description)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.NotFound, "Въпросът не е в този опит.");
        }

        var max = QuizGrading.MaxPoints(question);
        if (points is { } value && (value < 0 || value > max))
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Invalid, $"Точките трябва да са между 0 и {max}.");
        }

        var note = NormalizeComment(comment);
        if (note?.Length > AttemptAnswer.MaxCommentLength)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Invalid,
                $"Коментарът не може да бъде по-дълъг от {AttemptAnswer.MaxCommentLength} символа.");
        }

        var answer = attempt.Answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (answer?.CodeCheckPending == true)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Invalid, "Програмата още се проверява. Опитайте след малко.");
        }

        var rounded = points is { } given ? Math.Round(given, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
        if (answer == null)
        {
            if (rounded == null && note == null)
            {
                return ServiceResult<QuizAttempt>.Success(attempt); // a skipped question stays as it was
            }

            // A skipped question has no answer row yet: it gets one with nothing answered
            answer = new AttemptAnswer { QuestionId = questionId };
            attempt.Answers.Add(answer);
        }

        answer.ManualPoints = rounded;
        answer.ManualComment = note;
        answer.ManualGradedOn = rounded != null || note != null ? DateTime.UtcNow : null;

        var result = QuizAttemptResults.Build(attempt);
        attempt.Score = result.Score;
        attempt.MaxScore = result.MaxScore;

        await _context.SaveChangesAsync();
        return ServiceResult<QuizAttempt>.Success(attempt);
    }

    // The note as stored: one kind of line break, no spaces around it; empty means no note
    private static string? NormalizeComment(string? comment)
    {
        var text = (comment ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return text.Length == 0 ? null : text;
    }
}
