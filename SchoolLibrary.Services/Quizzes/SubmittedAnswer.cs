namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// What a student submitted for one question. It holds a field for each kind of response; a question
/// type reads only the fields it needs. Single-choice and true/false questions use <see cref="OptionId"/>,
/// multiple-choice questions use <see cref="OptionIds"/>, short-answer questions use <see cref="Text"/>.
/// </summary>
public sealed record SubmittedAnswer(int? OptionId = null, IReadOnlyList<int>? OptionIds = null, string? Text = null)
{
    /// <summary>The longest typed answer that is kept (the database column's size); longer text is cut.</summary>
    public const int MaxTextLength = 4000;

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

    /// <summary>A typed answer (trimmed, cut to <see cref="MaxTextLength"/>), or <see cref="Empty"/> when blank.</summary>
    public static SubmittedAnswer Typed(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return Empty;
        }

        return new SubmittedAnswer(Text: value.Length > MaxTextLength ? value[..MaxTextLength] : value);
    }

    /// <summary>Wraps picked options (question id → option id) as submitted answers.</summary>
    public static IReadOnlyDictionary<int, SubmittedAnswer> FromChoices(IReadOnlyDictionary<int, int?>? choices)
        => FromForm(choices, null);

    /// <summary>
    /// Combines the parts of a submitted quiz form: one picked option per question (<paramref name="choices"/>),
    /// the ticked options of multiple-choice questions (<paramref name="ticked"/>) and typed answers
    /// (<paramref name="typed"/>). For a question that appears in more than one part, the later part wins.
    /// </summary>
    public static IReadOnlyDictionary<int, SubmittedAnswer> FromForm(
        IReadOnlyDictionary<int, int?>? choices, IReadOnlyDictionary<int, List<int>>? ticked,
        IReadOnlyDictionary<int, string?>? typed = null)
    {
        var answers = (choices ?? new Dictionary<int, int?>()).ToDictionary(c => c.Key, c => Choice(c.Value));
        foreach (var (questionId, optionIds) in ticked ?? new Dictionary<int, List<int>>())
        {
            answers[questionId] = Choices(optionIds);
        }

        foreach (var (questionId, text) in typed ?? new Dictionary<int, string?>())
        {
            answers[questionId] = Typed(text);
        }

        return answers;
    }
}
