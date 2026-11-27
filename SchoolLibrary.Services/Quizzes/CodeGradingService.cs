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

        // The wrong checks each attempt made on these questions take points off
        var attemptIds = waiting.Select(a => a.AttemptId).Distinct().ToList();
        var questionIds = waiting.Select(a => a.QuestionId).Distinct().ToList();
        var wrongChecks = await _context.CodeChecks
            .Where(c => c.IsWrong && attemptIds.Contains(c.AttemptId) && questionIds.Contains(c.QuestionId))
            .GroupBy(c => new { c.AttemptId, c.QuestionId })
            .Select(g => new { g.Key.AttemptId, g.Key.QuestionId, Count = g.Count() })
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

            var wrong = wrongChecks.FirstOrDefault(w => w.AttemptId == answer.AttemptId && w.QuestionId == answer.QuestionId)?.Count ?? 0;
            var outcome = CodePenalty.Apply(grade, answer.Question, wrong);
            answer.AwardedPoints = outcome.AwardedPoints;
            answer.IsCorrect = outcome.IsCorrect;
            answer.CodeResults = outcome.Results?.ToJson();
            answer.CodeCheckPending = false;
            graded++;
        }

        await _context.SaveChangesAsync(cancellationToken);

        // An attempt's score is the sum of its answers' points (the teacher's where given)
        var gradedAttemptIds = waiting.Where(a => !a.CodeCheckPending).Select(a => a.AttemptId).Distinct().ToList();
        if (gradedAttemptIds.Count > 0)
        {
            var attempts = await _context.QuizAttempts.Where(a => gradedAttemptIds.Contains(a.Id)).ToListAsync(cancellationToken);
            var scores = await _context.AttemptAnswers
                .Where(a => gradedAttemptIds.Contains(a.AttemptId))
                .GroupBy(a => a.AttemptId)
                .Select(g => new { AttemptId = g.Key, Score = g.Sum(a => a.ManualPoints ?? a.AwardedPoints) })
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
