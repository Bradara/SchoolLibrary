using System.Text.RegularExpressions;
using SchoolLibrary.Data.Models;

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

    /// <summary>
    /// The text cut at its markers into text and picture parts, in order. A marker whose picture does not
    /// exist is dropped; the text parts are kept as written (the view encodes them).
    /// </summary>
    public static IReadOnlyList<QuestionTextPart> Split(string? text, IEnumerable<QuestionImage>? images)
    {
        var parts = new List<QuestionTextPart>();
        if (string.IsNullOrEmpty(text))
        {
            return parts;
        }

        var byNumber = (images ?? Enumerable.Empty<QuestionImage>())
            .GroupBy(i => i.Number)
            .ToDictionary(g => g.Key, g => g.First());

        var position = 0;
        foreach (Match match in MarkerRegex().Matches(text))
        {
            AddText(parts, text[position..match.Index]);
            if (byNumber.TryGetValue(int.Parse(match.Groups[1].Value), out var image))
            {
                parts.Add(new QuestionTextPart(null, image));
            }

            position = match.Index + match.Length;
        }

        AddText(parts, text[position..]);
        return parts;
    }

    /// <summary>The pictures that no marker in the given texts refers to, by number (shown after the text).</summary>
    public static IReadOnlyList<QuestionImage> Unplaced(IEnumerable<QuestionImage>? images, params string?[] texts)
    {
        var placed = texts.SelectMany(NumbersIn).ToHashSet();
        return (images ?? Enumerable.Empty<QuestionImage>())
            .Where(i => !placed.Contains(i.Number))
            .OrderBy(i => i.Number)
            .ToList();
    }

    /// <summary>The text for places that cannot show a picture (a drop-down, a title): markers become "[изображение]".</summary>
    public static string ToPlainText(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : MarkerRegex().Replace(text, "[изображение]");

    private static void AddText(List<QuestionTextPart> parts, string text)
    {
        if (text.Length > 0)
        {
            parts.Add(new QuestionTextPart(text, null));
        }
    }

    [GeneratedRegex(@"\[\[img:(\d{1,2})\]\]")]
    private static partial Regex MarkerRegex();
}

/// <summary>A piece of a question text: either plain text or a picture.</summary>
public sealed record QuestionTextPart(string? Text, QuestionImage? Image);
