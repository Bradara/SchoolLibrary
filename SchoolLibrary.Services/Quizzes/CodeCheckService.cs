using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

public class CodeCheckService : ICodeCheckService
{
    private readonly ApplicationDbContext _context;
    private readonly CodeGrader? _grader;

    /// <param name="grader">Runs the programs; null when no sandbox server is configured.</param>
    public CodeCheckService(ApplicationDbContext context, CodeGrader? grader = null)
    {
        _context = context;
        _grader = grader;
    }

    public async Task<ServiceResult<CodeCheckResult>> CheckPracticeAsync(int quizId, int questionId, string? code)
    {
        var quiz = await _context.Quizzes.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == quizId && q.Mode == QuizMode.Practice && q.IsPublished);
        var question = await LoadCodeQuestionAsync(questionId);
        if (quiz == null || question == null || !await IsInQuizAsync(quizId, question))
        {
            return ServiceResult<CodeCheckResult>.Failure(ServiceError.NotFound, "Въпросът не е намерен.");
        }

        var check = await RunAsync(question, code);
        return check.Error == ServiceError.None
            ? ServiceResult<CodeCheckResult>.Success(check.Result!)
            : ServiceResult<CodeCheckResult>.Failure(check.Error, check.Message!);
    }

    public async Task<ServiceResult<CodeCheckResult>> CheckExamAsync(Guid token, int questionId, string? code)
    {
        var attempt = await _context.QuizAttempts
            .Include(a => a.Questions)
            .FirstOrDefaultAsync(a => a.Token == token);
        if (attempt == null)
        {
            return ServiceResult<CodeCheckResult>.Failure(ServiceError.NotFound, "Опитът не е намерен.");
        }

        // Only a question this attempt was given (older attempts without a list: the quiz's own questions)
        var question = await LoadCodeQuestionAsync(questionId);
        var given = attempt.Questions.Count > 0
            ? attempt.Questions.Any(q => q.QuestionId == questionId)
            : question?.QuizId == attempt.QuizId;
        if (question == null || !given)
        {
            return ServiceResult<CodeCheckResult>.Failure(ServiceError.NotFound, "Въпросът не е намерен.");
        }

        if (attempt.SubmittedOn != null)
        {
            return ServiceResult<CodeCheckResult>.Failure(ServiceError.Conflict, "Изпитът вече е предаден.");
        }

        if (ExamTiming.LateBySeconds(attempt, DateTime.UtcNow) != null)
        {
            return ServiceResult<CodeCheckResult>.Failure(ServiceError.Conflict, "Времето за изпита изтече.");
        }

        var checks = await _context.CodeChecks
            .Where(c => c.AttemptId == attempt.Id && c.QuestionId == questionId)
            .Select(c => c.IsWrong)
            .ToListAsync();
        if (checks.Count >= CodePenalty.MaxChecksPerQuestion)
        {
            return ServiceResult<CodeCheckResult>.Failure(ServiceError.Conflict,
                $"Достигнахте най-многото проверки за този въпрос ({CodePenalty.MaxChecksPerQuestion}).");
        }

        var check = await RunAsync(question, code);
        if (check.Error != ServiceError.None)
        {
            return ServiceResult<CodeCheckResult>.Failure(check.Error, check.Message!);
        }

        var result = check.Result!;
        var wrong = checks.Count(w => w);
        var made = checks.Count;
        if (result.Counted)
        {
            _context.CodeChecks.Add(new CodeCheck
            {
                AttemptId = attempt.Id, QuestionId = questionId, CheckedOn = DateTime.UtcNow, IsWrong = !result.Passed
            });
            await _context.SaveChangesAsync();
            made++;
            wrong += result.Passed ? 0 : 1;
        }

        return ServiceResult<CodeCheckResult>.Success(result with
        {
            WrongChecks = wrong,
            ChecksLeft = CodePenalty.MaxChecksPerQuestion - made
        });
    }

    // Runs the program. An empty one or a missing server is refused; a run the server could not make is returned
    // but not counted. Hidden tests show only whether they passed.
    private async Task<(CodeCheckResult? Result, ServiceError Error, string? Message)> RunAsync(Question question, string? code)
    {
        var program = SubmittedAnswer.Program(code).Text;
        if (string.IsNullOrWhiteSpace(program))
        {
            return (null, ServiceError.Invalid, "Напишете програма преди проверката.");
        }

        if (_grader == null)
        {
            return (null, ServiceError.Conflict, "Проверката на код не е настроена.");
        }

        var grade = await _grader.GradeAsync(question, program);
        var counted = grade.IsFinal && grade.Status is not (CodeGradeStatus.Unanswered or CodeGradeStatus.TooLong);
        var passed = grade.Status == CodeGradeStatus.Graded && grade.Tests.All(t => t.Passed);
        var tests = grade.Tests
            .Select(t => t.Test.IsHidden
                ? new CodeCheckTest(t.Passed, true, null, null, null, null, t.TimedOut)
                : new CodeCheckTest(t.Passed, false, t.Test.Stdin, t.Test.Expected, t.Output, t.Error, t.TimedOut))
            .ToList();
        var message = grade.Status == CodeGradeStatus.NotChecked
            ? "Сървърът за проверка не е достъпен. Опитайте отново (тази проверка не се брои)."
            : grade.Message;
        return (new CodeCheckResult(grade.Status, message, passed, counted, tests, question.CheckPenaltyPercent,
            CodePenalty.For(question, 1)), ServiceError.None, null);
    }

    private Task<Question?> LoadCodeQuestionAsync(int questionId)
        => _context.Questions.AsNoTracking()
            .Include(q => q.TestCases)
            .FirstOrDefaultAsync(q => q.Id == questionId && q.Type == QuestionType.Code);

    // A practice question is the quiz's own, pinned from the bank, or drawn from one of its sections
    private async Task<bool> IsInQuizAsync(int quizId, Question question)
        => question.QuizId == quizId
            || await _context.QuizItems.AnyAsync(i => i.QuizId == quizId
                && ((i.Kind == QuizItemKind.FixedQuestion && i.QuestionId == question.Id)
                    || (i.Kind == QuizItemKind.RandomFromSection && question.SectionId != null && i.SectionId == question.SectionId)));
}
