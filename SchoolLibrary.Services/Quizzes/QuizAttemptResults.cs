using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Rebuilds what an exam attempt looked like and how it was graded, from what was stored.</summary>
public static class QuizAttemptResults
{
    /// <summary>Regrades the attempt from the options the student picked (stored on the attempt).</summary>
    public static QuizResult Build(QuizAttempt attempt)
    {
        var answers = attempt.Answers.ToDictionary(a => a.QuestionId, a => SubmittedAnswer.Choice(a.SelectedOptionId));
        return QuizGrading.Grade(attempt.Quiz, SnapshotQuestions(attempt), answers);
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
