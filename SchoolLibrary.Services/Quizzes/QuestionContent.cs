using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Copies a validated <see cref="QuestionInput"/> onto a question: text, type, points and options.</summary>
public static class QuestionContent
{
    /// <summary>
    /// Sets the question's text, type, points and case sensitivity and replaces its options with the input's. The caller
    /// removes the old option rows from the context when the question is already saved.
    /// </summary>
    public static void Apply(Question question, QuestionInput input)
    {
        question.Text = input.Text.Trim();
        question.Type = input.Type;
        question.Points = input.Points;
        question.CaseSensitive = input.Type == QuestionType.ShortAnswer && input.CaseSensitive;

        question.Options.Clear();
        for (var i = 0; i < input.Options.Count; i++)
        {
            // Explicit shares (multiple choice, short answer) or full credit for the one correct option
            var fraction = input.Fractions != null
                ? input.Fractions[i]
                : i == input.CorrectIndex ? AnswerOption.FullCredit : 0m;
            question.Options.Add(new AnswerOption
            {
                Text = input.Options[i].Trim(),
                Fraction = fraction,
                Order = i + 1
            });
        }
    }
}
