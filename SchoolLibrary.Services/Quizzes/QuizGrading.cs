using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Grades the answers to a quiz. Pure logic: it reads a loaded quiz and the submitted answers.</summary>
public static class QuizGrading
{
    /// <summary>
    /// Grades <paramref name="answers"/> (question id → submitted answer; a missing entry means unanswered)
    /// against <paramref name="quiz"/>. Each question is graded by the grader for its type. The score is the
    /// sum of the awarded points, and the maximum is the sum of every question's points.
    /// </summary>
    public static QuizResult Grade(Quiz quiz, IReadOnlyDictionary<int, SubmittedAnswer> answers)
        => Grade(quiz, quiz.Questions.OrderBy(q => q.Order), answers);

    /// <summary>
    /// Grades a specific, ordered set of questions (the attempt's snapshot) rather than the quiz's own
    /// question collection. Used for item-based quizzes where each attempt is served its own questions.
    /// </summary>
    public static QuizResult Grade(Quiz quiz, IEnumerable<Question> questions, IReadOnlyDictionary<int, SubmittedAnswer> answers)
    {
        var results = new List<QuestionResult>();

        foreach (var question in questions)
        {
            var answer = answers.TryGetValue(question.Id, out var submitted) && submitted != null
                ? submitted
                : SubmittedAnswer.Empty;

            var grade = QuestionGraders.For(question.Type).Grade(question, answer);
            results.Add(new QuestionResult(question, grade.Answer, grade.AwardedPoints, grade.IsCorrect));
        }

        var score = results.Sum(r => r.AwardedPoints);
        var maxScore = results.Sum(r => (decimal)r.Question.Points);
        return new QuizResult(quiz, score, maxScore, results);
    }
}
