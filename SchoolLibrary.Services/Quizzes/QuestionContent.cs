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
        // A description is worth nothing, whatever the form sent
        question.Points = input.Type == QuestionType.Description ? 0 : input.Points;
        question.CaseSensitive = input.Type == QuestionType.ShortAnswer && input.CaseSensitive;
        ApplyCode(question, input);

        question.Options.Clear();
        if (input.Type == QuestionType.Description)
        {
            return;
        }

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

    /// <summary>
    /// Sets a code question's language, starting code and scoring and replaces its tests with the input's
    /// (the caller removes the old test rows from the context). Other types get no code settings and no tests.
    /// </summary>
    private static void ApplyCode(Question question, QuestionInput input)
    {
        question.TestCases.Clear();
        var code = input.Type == QuestionType.Code ? input.Code : null;
        question.CodeLanguage = code?.Language;
        question.Statement = string.IsNullOrWhiteSpace(code?.Statement) ? null : code.Statement.Replace("\r\n", "\n").Trim();
        question.AnswerPreload = string.IsNullOrWhiteSpace(code?.Preload) ? null : code.Preload.TrimEnd();
        question.AllOrNothing = code?.AllOrNothing ?? true;
        if (code == null)
        {
            return;
        }

        for (var i = 0; i < code.Tests.Count; i++)
        {
            var test = code.Tests[i];
            question.TestCases.Add(new CodeTestCase
            {
                // Inputs and outputs are kept as typed (only the line endings unified): spaces may matter
                Stdin = (test.Stdin ?? string.Empty).Replace("\r\n", "\n"),
                Expected = (test.Expected ?? string.Empty).Replace("\r\n", "\n"),
                IsExample = test.IsExample,
                IsHidden = test.IsHidden,
                Weight = test.Weight,
                Order = i + 1
            });
        }
    }

    /// <summary>
    /// The ids of the question's options that the input keeps: each row's id from
    /// <see cref="QuestionInput.OptionIds"/> when it is one of the question's options (others, and repeats,
    /// count as new rows). A true/false question always keeps its two options, in order.
    /// </summary>
    public static IReadOnlyList<int?> KeptOptionIds(Question question, QuestionInput input)
    {
        var existing = question.Options.Select(o => o.Id).ToHashSet();
        var ids = input.OptionIds;
        if (input.Type == QuestionType.TrueFalse && question.Type == QuestionType.TrueFalse && ids == null)
        {
            ids = question.Options.OrderBy(o => o.Order).Select(o => (int?)o.Id).ToList();
        }

        var used = new HashSet<int>();
        return Enumerable.Range(0, input.Type == QuestionType.Description ? 0 : input.Options.Count)
            .Select(i => ids != null && i < ids.Count && ids[i] is int id && existing.Contains(id) && used.Add(id) ? (int?)id : null)
            .ToList();
    }

    /// <summary>
    /// Applies the input like <see cref="Apply"/>, but edits the kept options in place (see
    /// <see cref="KeptOptionIds"/>) instead of replacing them, so answers that point at them stay valid.
    /// New rows are added; the options that are not kept are taken off the question and returned for the
    /// caller to delete.
    /// </summary>
    public static IReadOnlyList<AnswerOption> ApplyInPlace(Question question, QuestionInput input)
    {
        var keptIds = KeptOptionIds(question, input);
        var existing = question.Options.ToDictionary(o => o.Id);

        // The new content, built the usual way on a scratch question
        var fresh = new Question();
        Apply(fresh, input);
        question.Text = fresh.Text;
        question.Type = fresh.Type;
        question.Points = fresh.Points;
        question.CaseSensitive = fresh.CaseSensitive;

        var options = fresh.Options.ToList();
        for (var i = 0; i < options.Count; i++)
        {
            if (keptIds[i] is int id)
            {
                var option = existing[id];
                option.Text = options[i].Text;
                option.Fraction = options[i].Fraction;
                option.Tolerance = options[i].Tolerance;
                option.MatchText = options[i].MatchText;
                option.Order = options[i].Order;
            }
            else
            {
                question.Options.Add(options[i]);
            }
        }

        var kept = keptIds.OfType<int>().ToHashSet();
        var removed = existing.Values.Where(o => !kept.Contains(o.Id)).ToList();
        foreach (var option in removed)
        {
            question.Options.Remove(option);
        }

        return removed;
    }
}
