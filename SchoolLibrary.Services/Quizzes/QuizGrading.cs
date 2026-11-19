using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Grades the answers to a quiz. Pure logic: it reads a loaded quiz and the picked options.</summary>
public static class QuizGrading
{
    /// <summary>
    /// Grades <paramref name="answers"/> (question id → picked option id, or null when left unanswered)
    /// against <paramref name="quiz"/>. A single-choice question is correct when the picked option is
    /// the one marked correct. The score is the sum of the points of the correctly answered questions,
    /// and the maximum is the sum of every question's points.
    /// </summary>
    public static QuizResult Grade(Quiz quiz, IReadOnlyDictionary<int, int?> answers)
    {
        var results = new List<QuestionResult>();

        foreach (var question in quiz.Questions.OrderBy(q => q.Order))
        {
            var correct = question.Options.FirstOrDefault(o => o.IsCorrect);
            answers.TryGetValue(question.Id, out var selected);

            var isCorrect = selected != null && correct != null && selected == correct.Id;
            results.Add(new QuestionResult(question, selected, correct?.Id, isCorrect));
        }

        var score = results.Where(r => r.IsCorrect).Sum(r => r.Question.Points);
        var maxScore = quiz.Questions.Sum(q => q.Points);
        return new QuizResult(quiz, score, maxScore, results);
    }
}
