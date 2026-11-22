using System.Text.Json;
using System.Text.Json.Serialization;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// One test's stored result with a copy of the test as it was run (input, expected output, hidden), so the
/// review shows what was checked even after the teacher edits the tests. <see cref="TestId"/> is the test case.
/// </summary>
public record StoredCodeTest(int TestId, bool Passed, string Output, string Error, bool TimedOut,
    string Stdin = "", string Expected = "", bool IsHidden = false);

/// <summary>
/// The stored result of running a code answer (<c>AttemptAnswer.CodeResults</c>): how the run ended, the
/// compiler's message and every test's result. Kept as JSON because it is only shown, never queried.
/// </summary>
public record StoredCodeResults(CodeGradeStatus Status, string? Message, IReadOnlyList<StoredCodeTest> Tests)
{
    /// <summary>The longest output or error kept per test; the rest is cut.</summary>
    public const int MaxTextLength = 2000;

    /// <summary>The longest compiler message kept.</summary>
    public const int MaxMessageLength = 4000;

    /// <summary>How many wrong checks were made before submitting.</summary>
    public int WrongChecks { get; init; }

    /// <summary>The points taken off for the wrong checks (never more than the program earned).</summary>
    public decimal PenaltyPoints { get; init; }

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>What is stored for <paramref name="grade"/>, with long texts cut.</summary>
    public static StoredCodeResults From(CodeGrade grade) => new(
        grade.Status,
        Cut(grade.Message, MaxMessageLength),
        grade.Tests
            .Select(t => new StoredCodeTest(t.Test.Id, t.Passed, Cut(t.Output, MaxTextLength)!, Cut(t.Error, MaxTextLength)!, t.TimedOut,
                Cut(t.Test.Stdin, MaxTextLength)!, Cut(t.Test.Expected, MaxTextLength)!, t.Test.IsHidden))
            .ToList());

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>The result stored when no sandbox server is configured or the run could not be made.</summary>
    public static StoredCodeResults NotChecked(string message) => new(CodeGradeStatus.NotChecked, message, []);

    /// <summary>Reads stored results; null when there are none or the text cannot be read.</summary>
    public static StoredCodeResults? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StoredCodeResults>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Cut(string? text, int length)
        => text is null || text.Length <= length ? text : text[..length] + "\n…";
}
