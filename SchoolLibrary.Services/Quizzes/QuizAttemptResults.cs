using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Rebuilds what an exam attempt looked like and how it was graded, from what was stored.</summary>
public static class QuizAttemptResults
{
    /// <summary>
    /// Regrades the attempt from the answers the student gave (stored on the attempt), with the points the teacher
    /// gave by hand in place of the automatic ones. This is what the attempt is worth and what is shown.
    /// </summary>
    public static QuizResult Build(QuizAttempt attempt)
        => ApplyManualPoints(BuildAutomatic(attempt), attempt) with { Grading = ExamGrading.For(attempt.ExamAssignment) };

    /// <summary>
    /// Regrades the attempt from the answers the student gave, ignoring the teacher's points: the automatic
    /// points that regrading stores in <see cref="AttemptAnswer.AwardedPoints"/>.
    /// </summary>
    public static QuizResult BuildAutomatic(QuizAttempt attempt)
    {
        var questions = SnapshotQuestions(attempt);

        // A matching pair is stored as option ids; the answer text is read back from the matched option
        var matchTexts = questions.SelectMany(q => q.Options).ToDictionary(o => o.Id, o => o.MatchText);

        var answers = attempt.Answers.ToDictionary(a => a.QuestionId, a =>
            a.ResponseText != null ? SubmittedAnswer.Typed(a.ResponseText)
            : a.Choices.Any(c => c.MatchedOptionId != null) ? SubmittedAnswer.Matched(a.Choices
                .Where(c => c.MatchedOptionId != null)
                .ToDictionary(c => c.OptionId, c => matchTexts.GetValueOrDefault(c.MatchedOptionId!.Value)))
            : a.Choices.Count > 0 ? SubmittedAnswer.Choices(a.Choices.Select(c => c.OptionId))
            : SubmittedAnswer.Choice(a.SelectedOptionId));

        // A program is not run again here: its stored points and results are shown as they were
        var codeIds = questions.Where(q => q.Type == SchoolLibrary.Data.Enums.QuestionType.Code).Select(q => q.Id).ToHashSet();
        var code = attempt.Answers
            .Where(a => codeIds.Contains(a.QuestionId))
            .ToDictionary(a => a.QuestionId, a => new CodeOutcome(a.ResponseText ?? string.Empty, a.AwardedPoints, a.IsCorrect,
                StoredCodeResults.FromJson(a.CodeResults), a.CodeCheckPending));
        return QuizGrading.Grade(attempt.Quiz, questions, answers, code);
    }

    // The teacher's points replace the automatic ones (never above what the question is worth now); the score is
    // their sum. A description earns nothing, whatever is stored.
    private static QuizResult ApplyManualPoints(QuizResult result, QuizAttempt attempt)
    {
        var manual = attempt.Answers
            .Where(a => a.ManualPoints != null || a.ManualComment != null)
            .ToDictionary(a => a.QuestionId);
        if (manual.Count == 0)
        {
            return result;
        }

        var questions = result.Questions.Select(r =>
        {
            if (r.IsDescription || !manual.TryGetValue(r.Question.Id, out var answer))
            {
                return r;
            }

            if (answer.ManualPoints is not { } points)
            {
                return r with { ManualComment = answer.ManualComment };
            }

            var max = QuizGrading.MaxPoints(r.Question);
            var given = Math.Min(points, max);
            return r with
            {
                AwardedPoints = given,
                IsCorrect = max > 0 && given >= max,
                IsManual = true,
                AutomaticPoints = r.AwardedPoints,
                ManualComment = answer.ManualComment
            };
        }).ToList();

        return result with { Score = questions.Sum(q => q.AwardedPoints), Questions = questions };
    }

    /// <summary>
    /// The questions served to an attempt, in order. Falls back to the quiz's own questions for any
    /// attempt created before snapshots existed.
    /// </summary>
    public static IReadOnlyList<Question> SnapshotQuestions(QuizAttempt attempt)
        => attempt.Questions.Count > 0
            ? attempt.Questions.OrderBy(aq => aq.Order).Select(aq => aq.Question).ToList()
            : attempt.Quiz.Questions.OrderBy(q => q.Order).ToList();

    /// <summary>
    /// Puts a non-tracked attempt's options in the order the student got (stable across reloads).
    /// Does nothing when the quiz does not shuffle options.
    /// </summary>
    public static void ApplyOptionOrder(QuizAttempt? attempt)
    {
        if (attempt == null || !attempt.Quiz.ShuffleOptions)
        {
            return;
        }

        foreach (var attemptQuestion in attempt.Questions)
        {
            QuizShuffle.ShuffleOptionsForAttempt(attemptQuestion.Question, attempt.Token);
        }
    }
}
