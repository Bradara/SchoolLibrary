using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Grades a typed number. An accepted value matches when the typed number is within its tolerance
/// (|typed − value| ≤ tolerance); "*" matches any number. Of the matching values the one worth the most
/// counts. Text that is not a number earns nothing but is kept, for the review.
/// </summary>
public sealed class NumericalGrader : IQuestionGrader
{
    public QuestionType Type => QuestionType.Numerical;

    public QuestionGrade Grade(Question question, SubmittedAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(answer.Text))
        {
            return new QuestionGrade(0m, false, SubmittedAnswer.Empty);
        }

        var typed = SubmittedAnswer.Typed(answer.Text);
        var number = NumericAnswer.Parse(typed.Text);
        if (number == null)
        {
            return new QuestionGrade(0m, false, typed);
        }

        var best = question.Options
            .Where(o => Matches(o, number.Value))
            .OrderByDescending(o => o.Fraction)
            .FirstOrDefault();
        if (best == null)
        {
            return new QuestionGrade(0m, false, typed);
        }

        var share = Math.Clamp(best.Fraction, 0m, AnswerOption.FullCredit) / AnswerOption.FullCredit;
        var points = Math.Round(question.Points * share, 2, MidpointRounding.AwayFromZero);
        return new QuestionGrade(points, best.IsCorrect, typed);
    }

    private static bool Matches(AnswerOption option, decimal number)
    {
        if (option.Text == NumericAnswer.AnyNumber)
        {
            return true;
        }

        var value = NumericAnswer.Parse(option.Text);
        if (value == null)
        {
            return false;
        }

        try
        {
            return Math.Abs(number - value.Value) <= Math.Abs(option.Tolerance ?? 0m);
        }
        catch (OverflowException)
        {
            // Two huge numbers of opposite sign: their difference does not fit, so they are far apart
            return false;
        }
    }
}
