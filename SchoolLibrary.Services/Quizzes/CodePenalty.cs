using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// The penalty for checking a code answer before submitting it and getting it wrong: every wrong check takes
/// <see cref="Question.CheckPenaltyPercent"/> of the question's points off what the program finally earns,
/// never going below 0.
/// </summary>
public static class CodePenalty
{
    /// <summary>The most checks of one question in one exam attempt (keeps the sandbox server from being flooded).</summary>
    public const int MaxChecksPerQuestion = 30;

    /// <summary>The points taken off for <paramref name="wrongChecks"/> wrong checks, before limiting to what was earned.</summary>
    public static decimal For(Question question, int wrongChecks)
        => Math.Round(question.Points * question.CheckPenaltyPercent * Math.Clamp(wrongChecks, 0, MaxChecksPerQuestion) / 100m,
            2, MidpointRounding.AwayFromZero);

    /// <summary>The outcome of a graded program after the penalty for <paramref name="wrongChecks"/> wrong checks.</summary>
    public static CodeOutcome Apply(CodeGrade grade, Question question, int wrongChecks)
    {
        if (grade.Status == CodeGradeStatus.Unanswered)
        {
            return new CodeOutcome(grade.Code, 0m, false, null);
        }

        var wrong = Math.Clamp(wrongChecks, 0, MaxChecksPerQuestion);
        var penalty = Math.Min(For(question, wrong), grade.AwardedPoints);
        var points = grade.AwardedPoints - penalty;
        var results = StoredCodeResults.From(grade) with { WrongChecks = wrong, PenaltyPoints = penalty };
        return new CodeOutcome(grade.Code, points, grade.IsCorrect && penalty == 0m, results);
    }
}
