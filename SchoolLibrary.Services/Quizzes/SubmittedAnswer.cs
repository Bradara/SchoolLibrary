namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// What a student submitted for one question. It holds a field for each kind of response; a question
/// type reads only the fields it needs. Single-choice and true/false questions use <see cref="OptionId"/>,
/// multiple-choice questions use <see cref="OptionIds"/>.
/// </summary>
public sealed record SubmittedAnswer(int? OptionId = null, IReadOnlyList<int>? OptionIds = null)
{
    /// <summary>A question left unanswered.</summary>
    public static readonly SubmittedAnswer Empty = new();

    /// <summary>The ticked options (multiple choice); empty when none.</summary>
    public IReadOnlyList<int> TickedOptionIds => OptionIds ?? Array.Empty<int>();

    /// <summary>One picked option, or <see cref="Empty"/> when nothing was picked.</summary>
    public static SubmittedAnswer Choice(int? optionId) => optionId == null ? Empty : new SubmittedAnswer(optionId);

    /// <summary>Several ticked options (duplicates removed), or <see cref="Empty"/> when none were ticked.</summary>
    public static SubmittedAnswer Choices(IEnumerable<int>? optionIds)
    {
        var ids = (optionIds ?? Enumerable.Empty<int>()).Distinct().ToList();
        return ids.Count == 0 ? Empty : new SubmittedAnswer(OptionIds: ids);
    }

    /// <summary>Wraps picked options (question id → option id) as submitted answers.</summary>
    public static IReadOnlyDictionary<int, SubmittedAnswer> FromChoices(IReadOnlyDictionary<int, int?>? choices)
        => FromForm(choices, null);

    /// <summary>
    /// Combines the two parts of a submitted quiz form: one picked option per question
    /// (<paramref name="choices"/>) and the ticked options of multiple-choice questions
    /// (<paramref name="ticked"/>). Ticked options win for a question that appears in both.
    /// </summary>
    public static IReadOnlyDictionary<int, SubmittedAnswer> FromForm(
        IReadOnlyDictionary<int, int?>? choices, IReadOnlyDictionary<int, List<int>>? ticked)
    {
        var answers = (choices ?? new Dictionary<int, int?>()).ToDictionary(c => c.Key, c => Choice(c.Value));
        foreach (var (questionId, optionIds) in ticked ?? new Dictionary<int, List<int>>())
        {
            answers[questionId] = Choices(optionIds);
        }

        return answers;
    }
}
