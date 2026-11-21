using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Grades a typed short answer. The options are the accepted answers; of those that match (see
/// <see cref="ShortAnswerMatcher"/>) the one worth the most counts, so the order of the accepted answers does
/// not matter. A negative share counts as 0. The typed text is kept even when it is wrong, for the review.
/// </summary>
public sealed class ShortAnswerGrader : IQuestionGrader
{
    public QuestionType Type => QuestionType.ShortAnswer;

    public QuestionGrade Grade(Question question, SubmittedAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(answer.Text))
        {
            return new QuestionGrade(0m, false, SubmittedAnswer.Empty);
        }

        // Keep only the text: an option id sent with a typed answer means nothing here
        var typed = SubmittedAnswer.Typed(answer.Text);
        var best = question.Options
            .Where(o => ShortAnswerMatcher.Matches(o.Text, typed.Text, question.CaseSensitive))
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
}
