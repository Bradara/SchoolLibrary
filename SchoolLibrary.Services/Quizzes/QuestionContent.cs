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
            // Explicit shares (multiple choice, short answer, numerical) or full credit for the one correct option
            var fraction = input.Fractions != null
                ? input.Fractions[i]
                : i == input.CorrectIndex ? AnswerOption.FullCredit : 0m;
            var text = input.Options[i].Trim();
            decimal? tolerance = null;
            if (input.Type == QuestionType.Numerical)
            {
                // Store the value with a decimal point ("3,14" -> "3.14") and a missing tolerance as exact
                var value = NumericAnswer.Parse(text);
                text = value.HasValue ? NumericAnswer.Format(value.Value) : NumericAnswer.AnyNumber;
                tolerance = input.Tolerances?[i] ?? 0m;
            }

            // The right side of a matching pair
            var match = input.Type == QuestionType.Matching ? input.Matches?[i].Trim() : null;

            question.Options.Add(new AnswerOption
            {
                Text = text,
                Fraction = fraction,
                Tolerance = tolerance,
                MatchText = match,
                Order = i + 1
            });
        }
    }
}
