using System.Text;

namespace SchoolLibrary.Services.Reading;

/// <summary>Tidies text that comes from the pages of a book (bookmark labels, highlighted passages).</summary>
public static class ReaderText
{
    /// <summary>
    /// One line: runs of white space and control characters become one space, the ends are trimmed, and a text
    /// longer than <paramref name="maxLength"/> is cut with an ellipsis. Empty text gives "".
    /// </summary>
    public static string OneLine(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = new StringBuilder(Math.Min(text.Length, maxLength + 1));
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = result.Length > 0;
                continue;
            }

            if (space)
            {
                result.Append(' ');
                space = false;
            }

            result.Append(c);
            if (result.Length > maxLength)
            {
                break;
            }
        }

        if (result.Length > maxLength)
        {
            result.Length = maxLength - 1;
            result.Append('…');
        }

        return result.ToString();
    }
}
