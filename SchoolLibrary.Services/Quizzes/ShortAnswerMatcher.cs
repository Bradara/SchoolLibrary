using System.Text;
using System.Text.RegularExpressions;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Compares a typed short answer with an accepted answer. Both are normalised the same way: Unicode
/// composed form, spaces trimmed and collapsed, letter case ignored unless the question is case-sensitive,
/// and Latin letters that look like Cyrillic ones (a, e, o, p, c, x, y, …) treated as Cyrillic, so a
/// student who types "кoт" with a Latin "o" still matches "кот". In an accepted answer <c>*</c> matches any
/// text (including none) and <c>\*</c> is a literal asterisk, as in Moodle.
/// </summary>
public static class ShortAnswerMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled, MatchTimeout);

    // Latin capitals that look the same as Cyrillic ones; used after upper-casing (case-insensitive)
    private static readonly Dictionary<char, char> UpperLookalikes = new()
    {
        ['A'] = 'А', ['B'] = 'В', ['C'] = 'С', ['E'] = 'Е', ['H'] = 'Н', ['K'] = 'К', ['M'] = 'М',
        ['O'] = 'О', ['P'] = 'Р', ['T'] = 'Т', ['X'] = 'Х', ['Y'] = 'У'
    };

    // Lower-case Latin letters that look the same as Cyrillic ones; added for case-sensitive questions
    private static readonly Dictionary<char, char> LowerLookalikes = new()
    {
        ['a'] = 'а', ['c'] = 'с', ['e'] = 'е', ['o'] = 'о', ['p'] = 'р', ['x'] = 'х', ['y'] = 'у'
    };

    /// <summary>Whether <paramref name="typed"/> matches the accepted answer <paramref name="accepted"/>.</summary>
    public static bool Matches(string? accepted, string? typed, bool caseSensitive)
    {
        var answer = Normalize(typed, caseSensitive);
        if (answer.Length == 0 || string.IsNullOrWhiteSpace(accepted))
        {
            return false;
        }

        var pattern = Normalize(accepted, caseSensitive);
        if (!pattern.Contains('*'))
        {
            return string.Equals(pattern, answer, StringComparison.Ordinal);
        }

        try
        {
            return Regex.IsMatch(answer, ToRegex(pattern), RegexOptions.Singleline | RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological pattern must not hang the request; treat it as no match
            return false;
        }
    }

    /// <summary>The text as it is compared: composed, single-spaced, trimmed, look-alike letters unified.</summary>
    public static string Normalize(string? text, bool caseSensitive)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var value = Whitespace.Replace(text.Normalize(NormalizationForm.FormC), " ").Trim();
        if (!caseSensitive)
        {
            value = value.ToUpperInvariant();
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(UpperLookalikes.TryGetValue(ch, out var upper) ? upper
                : caseSensitive && LowerLookalikes.TryGetValue(ch, out var lower) ? lower
                : ch);
        }

        return builder.ToString();
    }

    // "*" becomes "any text", "\*" a literal asterisk, everything else is matched literally
    private static string ToRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        var literal = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '\\' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                literal.Append('*');
                i++;
            }
            else if (pattern[i] == '*')
            {
                builder.Append(Regex.Escape(literal.ToString())).Append(".*");
                literal.Clear();
            }
            else
            {
                literal.Append(pattern[i]);
            }
        }

        builder.Append(Regex.Escape(literal.ToString())).Append('$');
        return builder.ToString();
    }
}
