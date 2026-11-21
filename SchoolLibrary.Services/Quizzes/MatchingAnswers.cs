using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// The two sides of a matching question. The items on the left are the options with text; the answers on the
/// right are the distinct <see cref="AnswerOption.MatchText"/> values (including those of extra wrong answers).
/// The student picks an answer by its text, so the form never carries option ids that could give the pairs away.
/// </summary>
public static class MatchingAnswers
{
    /// <summary>The items to match, in the question's (possibly shuffled) order.</summary>
    public static IReadOnlyList<AnswerOption> Items(Question question)
        => question.Options.Where(o => !string.IsNullOrWhiteSpace(o.Text)).ToList();

    /// <summary>
    /// The answers to choose from: each text once, in alphabetical order, so the list is the same on every
    /// load and its order says nothing about which item an answer belongs to.
    /// </summary>
    public static IReadOnlyList<string> Choices(Question question)
        => question.Options
            .Select(o => o.MatchText)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m, StringComparer.Ordinal)
            .ToList();

    /// <summary>The first option whose answer text is <paramref name="answer"/>; null when there is none.</summary>
    public static AnswerOption? OptionFor(Question question, string? answer)
        => answer == null
            ? null
            : question.Options.OrderBy(o => o.Order).FirstOrDefault(o => string.Equals(o.MatchText, answer, StringComparison.Ordinal));
}
