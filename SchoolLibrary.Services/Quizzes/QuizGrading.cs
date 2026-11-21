using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Grades the answers to a quiz. Pure logic: it reads a loaded quiz and the submitted answers.</summary>
public static class QuizGrading
{
    /// <summary>
    /// Grades <paramref name="answers"/> (question id → submitted answer; a missing entry means unanswered)
    /// against <paramref name="quiz"/>. Each question is graded by the grader for its type. The score is the
    /// sum of the awarded points, and the maximum is the sum of every question's points (descriptions add none).
    /// </summary>
    public static QuizResult Grade(Quiz quiz, IReadOnlyDictionary<int, SubmittedAnswer> answers)
        => Grade(quiz, quiz.Questions.OrderBy(q => q.Order), answers);

    /// <summary>
    /// Grades a specific, ordered set of questions (the attempt's snapshot) rather than the quiz's own
    /// question collection. Used for item-based quizzes where each attempt is served its own questions.
    /// </summary>
    /// <remarks>
    /// A code question is not run here: its outcome comes from <paramref name="code"/> (question id → the run's
    /// outcome, made beforehand on the sandbox server or read back from an attempt). Without one it earns nothing.
    /// </remarks>
    public static QuizResult Grade(Quiz quiz, IEnumerable<Question> questions, IReadOnlyDictionary<int, SubmittedAnswer> answers,
        IReadOnlyDictionary<int, CodeOutcome>? code = null)
    {
        var results = new List<QuestionResult>();

        foreach (var question in questions)
        {
            var answer = answers.TryGetValue(question.Id, out var submitted) && submitted != null
                ? submitted
                : SubmittedAnswer.Empty;

            if (question.Type == SchoolLibrary.Data.Enums.QuestionType.Code)
            {
                var outcome = code != null && code.TryGetValue(question.Id, out var known) ? known : null;
                var program = outcome?.Code ?? answer.Text;
                results.Add(new QuestionResult(question, string.IsNullOrEmpty(program) ? SubmittedAnswer.Empty : new SubmittedAnswer(Text: program),
                    outcome?.AwardedPoints ?? 0m, outcome?.IsCorrect ?? false) { CodeResults = outcome?.Results });
                continue;
            }

            var grade = QuestionGraders.For(question.Type).Grade(question, answer);
            results.Add(new QuestionResult(question, grade.Answer, grade.AwardedPoints, grade.IsCorrect));
        }

        var score = results.Sum(r => r.AwardedPoints);
        var maxScore = results.Sum(r => (decimal)MaxPoints(r.Question));
        return new QuizResult(quiz, score, maxScore, results);
    }

    /// <summary>The points a question adds to the maximum score: none for a description, whatever is stored.</summary>
    public static int MaxPoints(Question question)
        => question.Type == SchoolLibrary.Data.Enums.QuestionType.Description ? 0 : question.Points;
}
