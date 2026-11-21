namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// What a student submitted for one question. It holds a field for each kind of response; a question
/// type reads only the fields it needs. Single-choice and true/false questions use <see cref="OptionId"/>,
/// multiple-choice questions use <see cref="OptionIds"/>, short-answer and numerical questions use
/// <see cref="Text"/>, matching questions use <see cref="Pairs"/> (item option id → the answer text picked for it).
/// </summary>
public sealed record SubmittedAnswer(int? OptionId = null, IReadOnlyList<int>? OptionIds = null, string? Text = null,
    IReadOnlyDictionary<int, string>? Pairs = null)
{
    /// <summary>The longest typed answer that is kept; longer text is cut. Programs have their own limit (<see cref="CodeGrader.MaxCodeLength"/>).</summary>
    public const int MaxTextLength = 4000;

    /// <summary>A question left unanswered.</summary>
    public static readonly SubmittedAnswer Empty = new();

    /// <summary>The ticked options (multiple choice); empty when none.</summary>
    public IReadOnlyList<int> TickedOptionIds => OptionIds ?? Array.Empty<int>();

    /// <summary>The picked answers of a matching question (item option id → answer text); empty when none.</summary>
    public IReadOnlyDictionary<int, string> MatchedPairs => Pairs ?? new Dictionary<int, string>();

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

    /// <summary>
    /// A program (code question): kept as written except for white space at the end, cut to
    /// <see cref="CodeGrader.MaxCodeLength"/> (the answer box allows no more), or <see cref="Empty"/> when blank.
    /// </summary>
    public static SubmittedAnswer Program(string? code)
    {
        var value = (code ?? string.Empty).TrimEnd();
        if (value.Trim().Length == 0)
        {
            return Empty;
        }

        return new SubmittedAnswer(Text: value.Length > CodeGrader.MaxCodeLength ? value[..CodeGrader.MaxCodeLength] : value);
    }

    /// <summary>
    /// The answers picked for the items of a matching question (item option id → answer text). Blank picks
    /// are dropped; with none left the result is <see cref="Empty"/>.
    /// </summary>
    public static SubmittedAnswer Matched(IReadOnlyDictionary<int, string?>? pairs)
    {
        var picked = (pairs ?? new Dictionary<int, string?>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key, p => p.Value!.Trim());
        return picked.Count == 0 ? Empty : new SubmittedAnswer(Pairs: picked);
    }

    /// <summary>Wraps picked options (question id → option id) as submitted answers.</summary>
    public static IReadOnlyDictionary<int, SubmittedAnswer> FromChoices(IReadOnlyDictionary<int, int?>? choices)
        => FromForm(choices, null);

    /// <summary>
    /// Combines the parts of a submitted quiz form: one picked option per question (<paramref name="choices"/>),
    /// the ticked options of multiple-choice questions (<paramref name="ticked"/>), typed answers
    /// (<paramref name="typed"/>) and the picks of matching questions (<paramref name="matched"/>: question id →
    /// item option id → answer text) and programs (<paramref name="code"/>). For a question that appears in more
    /// than one part, the later part wins.
    /// </summary>
    public static IReadOnlyDictionary<int, SubmittedAnswer> FromForm(
        IReadOnlyDictionary<int, int?>? choices, IReadOnlyDictionary<int, List<int>>? ticked,
        IReadOnlyDictionary<int, string?>? typed = null,
        IReadOnlyDictionary<int, Dictionary<int, string?>>? matched = null,
        IReadOnlyDictionary<int, string?>? code = null)
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

        foreach (var (questionId, pairs) in matched ?? new Dictionary<int, Dictionary<int, string?>>())
        {
            answers[questionId] = Matched(pairs);
        }

        foreach (var (questionId, program) in code ?? new Dictionary<int, string?>())
        {
            answers[questionId] = Program(program);
        }

        return answers;
    }
}
