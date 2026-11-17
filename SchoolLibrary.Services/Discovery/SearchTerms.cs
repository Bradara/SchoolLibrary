using System.Text.RegularExpressions;

namespace SchoolLibrary.Services.Discovery;

/// <summary>
/// Turns what a teacher typed into the words worth searching for. Joining words such as "и", "на", "или" are
/// dropped (they match almost everything and make a search too strict), punctuation is removed, and words are
/// compared by their beginning so that "дроби" also finds "дроб" and "Египет" also finds "египетски".
/// </summary>
public static class SearchTerms
{
    public const int MaxTerms = 6;

    private static readonly Regex NotLettersOrDigits = new(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);

    // Bulgarian, Russian and English words that carry no topic
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "и", "или", "на", "в", "във", "с", "със", "за", "от", "по", "до", "при", "към", "че", "да", "е", "са", "ще", "не",
        "като", "как", "какво", "това", "този", "тази", "тези", "а", "но", "ли", "ни", "се", "си", "към", "около", "между",
        "для", "что", "как", "это", "или", "над", "под", "без", "при", "об",
        "and", "or", "not", "the", "of", "in", "on", "a", "an", "to", "for", "with", "by", "at", "is", "are"
    };

    /// <summary>The significant words of the text: lower case, without stop words, one-letter words and repeats, at most <see cref="MaxTerms"/>.</summary>
    public static IReadOnlyList<string> Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();

        return NotLettersOrDigits.Split(text)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= 2 && !StopWords.Contains(w))
            .Distinct()
            .Take(MaxTerms)
            .ToList();
    }

    /// <summary>The beginning of a word, so that other endings of the same word match too. Short words stay whole.</summary>
    public static string Stem(string word) => word.Length switch
    {
        <= 4 => word,
        <= 6 => word[..^1],
        _ => word[..^2]
    };

    /// <summary>How many of the terms start a word of the text.</summary>
    public static int CountMatches(string? text, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrWhiteSpace(text) || terms.Count == 0) return 0;

        var words = NotLettersOrDigits.Split(text).Where(w => w.Length > 0).ToList();
        return terms.Count(t =>
        {
            var stem = Stem(t);
            return words.Any(w => w.StartsWith(stem, StringComparison.OrdinalIgnoreCase));
        });
    }

    /// <summary>How many terms a result must contain when no result contains all of them: half of them, at least one.</summary>
    public static int RelaxedRequirement(int termCount) => Math.Max(1, (termCount + 1) / 2);
}
