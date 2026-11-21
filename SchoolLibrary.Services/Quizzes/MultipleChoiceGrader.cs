using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Grades a question where any number of options may be ticked. The shares of the ticked options are
/// added up and compared with the shares of all correct options, so ticking every correct option (and no
/// wrong one) earns all the points even when the shares are rounded (3 × 33.33). Wrong options have
/// negative shares; the result never drops below 0 or rises above the question's points.
/// </summary>
public sealed class MultipleChoiceGrader : IQuestionGrader
{
    public QuestionType Type => QuestionType.MultipleChoice;

    public QuestionGrade Grade(Question question, SubmittedAnswer answer)
    {
        // Only options of this question count, each once, in the question's own order
        var ticked = question.Options
            .Where(o => answer.TickedOptionIds.Contains(o.Id))
            .OrderBy(o => o.Order)
            .ToList();
        if (ticked.Count == 0)
        {
            return new QuestionGrade(0m, false, SubmittedAnswer.Empty);
        }

        var available = question.Options.Where(o => o.Fraction > 0).Sum(o => o.Fraction);
        var earned = ticked.Sum(o => o.Fraction);
        var share = available <= 0 ? 0m : Math.Clamp(earned / available, 0m, 1m);

        var points = Math.Round(question.Points * share, 2, MidpointRounding.AwayFromZero);
        return new QuestionGrade(points, share == 1m, SubmittedAnswer.Choices(ticked.Select(o => o.Id)));
    }
}
