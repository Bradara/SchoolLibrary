using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Grades a question with one pick. The student earns the picked option's share of the points (its
/// <see cref="AnswerOption.Fraction"/>); a negative share counts as 0, so a question never takes points away.
/// </summary>
public class SingleChoiceGrader : IQuestionGrader
{
    public virtual QuestionType Type => QuestionType.SingleChoice;

    public QuestionGrade Grade(Question question, SubmittedAnswer answer)
    {
        // Only an option of this question counts; anything else is treated as unanswered
        var picked = answer.OptionId == null
            ? null
            : question.Options.FirstOrDefault(o => o.Id == answer.OptionId);
        if (picked == null)
        {
            return new QuestionGrade(0m, false, SubmittedAnswer.Empty);
        }

        var share = Math.Clamp(picked.Fraction, 0m, AnswerOption.FullCredit) / AnswerOption.FullCredit;
        var points = Math.Round(question.Points * share, 2, MidpointRounding.AwayFromZero);
        return new QuestionGrade(points, picked.IsCorrect, SubmittedAnswer.Choice(picked.Id));
    }
}

/// <summary>Grades a true/false question: one pick out of "Вярно" and "Невярно", like a single-choice question.</summary>
public sealed class TrueFalseGrader : SingleChoiceGrader
{
    public override QuestionType Type => QuestionType.TrueFalse;
}
