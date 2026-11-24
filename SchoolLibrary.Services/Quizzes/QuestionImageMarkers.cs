using System.Text.RegularExpressions;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// The <c>[[img:N]]</c> markers that place a question's pictures in its plain text. The text itself
/// never holds HTML; the view replaces each marker with the picture that has that number.
/// </summary>
public static partial class QuestionImageMarkers
{
    /// <summary>The marker for the picture with the given number.</summary>
    public static string Marker(int number) => $"[[img:{number}]]";

    /// <summary>The numbers of the markers in the text, in order of appearance (repeats kept once).</summary>
    public static IReadOnlyList<int> NumbersIn(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<int>();
        }

        return MarkerRegex().Matches(text)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// The text without the markers of the given picture. A marker alone on its line takes its line
    /// with it, so no empty line is left behind.
    /// </summary>
    public static string? Remove(string? text, int number)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var marker = Regex.Escape(Marker(number));
        var withoutLines = Regex.Replace(text, $@"(?m)^[ \t]*{marker}[ \t]*(\r?\n|$)", string.Empty);
        return Regex.Replace(withoutLines, marker, string.Empty).TrimEnd();
    }

    [GeneratedRegex(@"\[\[img:(\d{1,2})\]\]")]
    private static partial Regex MarkerRegex();
}
