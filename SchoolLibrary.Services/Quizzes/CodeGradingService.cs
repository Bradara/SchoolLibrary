using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Checks waiting code answers on the sandbox server. The programs of one round run side by side (the line in
/// front of the server keeps how many at once); the results are then stored together and every touched
/// attempt's score is added up again from its answers.
/// </summary>
public class CodeGradingService : ICodeGradingService
{
    private readonly ApplicationDbContext _context;
    private readonly CodeGrader? _grader;

    /// <param name="grader">Runs the programs; null when no sandbox server is configured (nothing is checked).</param>
    public CodeGradingService(ApplicationDbContext context, CodeGrader? grader = null)
    {
        _context = context;
        _grader = grader;
    }

    public async Task<CodeGradingRun> GradePendingAsync(int maxAnswers, CancellationToken cancellationToken = default)
    {
        if (_grader == null)
        {
            return new CodeGradingRun(0, await CountPendingAsync(cancellationToken), ServerUnavailable: true);
        }

        var waiting = await _context.AttemptAnswers
            .Where(a => a.CodeCheckPending)
            .OrderBy(a => a.Id)
            .Take(Math.Max(1, maxAnswers))
            .Include(a => a.Question)
                .ThenInclude(q => q.TestCases)
            .ToListAsync(cancellationToken);

        // The programs run side by side; the database is only touched afterwards
        var grades = await Task.WhenAll(waiting.Select(answer => answer.Question.Type == QuestionType.Code
            ? _grader.GradeAsync(answer.Question, answer.ResponseText, cancellationToken)
            : Task.FromResult<CodeGrade?>(null)!));

        var graded = 0;
        var unavailable = false;
        for (var i = 0; i < waiting.Count; i++)
        {
            var answer = waiting[i];
            var grade = grades[i];
            if (grade == null)
            {
                // The question is no longer a code question: there is nothing to run
                answer.CodeCheckPending = false;
                continue;
            }

            if (!grade.IsFinal)
            {
                unavailable = true; // stays waiting for the next round
                continue;
            }

            answer.AwardedPoints = grade.AwardedPoints;
            answer.IsCorrect = grade.IsCorrect;
            answer.CodeResults = grade.Status == CodeGradeStatus.Unanswered ? null : StoredCodeResults.From(grade).ToJson();
            answer.CodeCheckPending = false;
            graded++;
        }

        await _context.SaveChangesAsync(cancellationToken);

        // An attempt's score is the sum of its answers' points
        var attemptIds = waiting.Where(a => !a.CodeCheckPending).Select(a => a.AttemptId).Distinct().ToList();
        if (attemptIds.Count > 0)
        {
            var attempts = await _context.QuizAttempts.Where(a => attemptIds.Contains(a.Id)).ToListAsync(cancellationToken);
            var scores = await _context.AttemptAnswers
                .Where(a => attemptIds.Contains(a.AttemptId))
                .GroupBy(a => a.AttemptId)
                .Select(g => new { AttemptId = g.Key, Score = g.Sum(a => a.AwardedPoints) })
                .ToDictionaryAsync(x => x.AttemptId, x => x.Score, cancellationToken);
            foreach (var attempt in attempts)
            {
                attempt.Score = scores.GetValueOrDefault(attempt.Id);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        return new CodeGradingRun(graded, await CountPendingAsync(cancellationToken), unavailable);
    }

    private Task<int> CountPendingAsync(CancellationToken cancellationToken)
        => _context.AttemptAnswers.CountAsync(a => a.CodeCheckPending, cancellationToken);
}
