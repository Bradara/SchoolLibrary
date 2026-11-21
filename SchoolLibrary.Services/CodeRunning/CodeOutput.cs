namespace SchoolLibrary.Services.CodeRunning;

/// <summary>Compares and tidies program output.</summary>
public static class CodeOutput
{
    // mono prints an unhandled exception twice; the second copy starts with this marker
    private const string MonoFatalMarker = "[ERROR] FATAL UNHANDLED EXCEPTION";

    /// <summary>
    /// The output as it is compared: line endings unified, white space at the end of every line and blank
    /// lines at the end removed (as the EqualityGrader of CodeRunner does).
    /// </summary>
    public static string Normalize(string? output)
    {
        var lines = (output ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToList();

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines);
    }

    /// <summary>Whether the program printed the expected output (see <see cref="Normalize"/>).</summary>
    public static bool Matches(string? expected, string? actual)
        => string.Equals(Normalize(expected), Normalize(actual), StringComparison.Ordinal);

    /// <summary>The error output with the repeated copy of a mono exception left out.</summary>
    public static string ShortenError(string? error)
    {
        var text = error ?? string.Empty;
        var repeat = text.IndexOf(MonoFatalMarker, StringComparison.Ordinal);
        return (repeat > 0 ? text[..repeat] : text).Trim();
    }
}
