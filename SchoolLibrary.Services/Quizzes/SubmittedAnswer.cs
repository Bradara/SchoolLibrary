namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// What a student submitted for one question. It holds a field for each kind of response; a question
/// type reads only the fields it needs. Single-choice questions use <see cref="OptionId"/>.
/// </summary>
public sealed record SubmittedAnswer(int? OptionId = null)
{
    /// <summary>A question left unanswered.</summary>
    public static readonly SubmittedAnswer Empty = new();

    /// <summary>One picked option, or <see cref="Empty"/> when nothing was picked.</summary>
    public static SubmittedAnswer Choice(int? optionId) => optionId == null ? Empty : new SubmittedAnswer(optionId);

    /// <summary>Wraps picked options (question id → option id) as submitted answers.</summary>
    public static IReadOnlyDictionary<int, SubmittedAnswer> FromChoices(IReadOnlyDictionary<int, int?>? choices)
        => (choices ?? new Dictionary<int, int?>()).ToDictionary(c => c.Key, c => Choice(c.Value));
}
