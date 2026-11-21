using System.Globalization;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Reading and writing the numbers of numerical questions. Students and teachers may type a decimal comma
/// or point ("3,14" or "3.14"), spaces between digit groups ("1 000") and a minus sign of either kind
/// ("-" or "−"). Numbers are stored with a decimal point and shown with a decimal comma.
/// </summary>
public static class NumericAnswer
{
    /// <summary>An accepted answer that matches any number (as in Moodle).</summary>
    public const string AnyNumber = "*";

    /// <summary>The longest text that is read as a number; anything longer is not a number.</summary>
    private const int MaxLength = 50;

    /// <summary>
    /// Reads <paramref name="text"/> as a number, or returns null when it is not one. Text with both a
    /// comma and a point ("1,000.5") is ambiguous and is not read.
    /// </summary>
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
        {
            return null;
        }

        var value = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray())
            .Replace('−', '-');
        if (value.Contains(',') && value.Contains('.'))
        {
            return null;
        }

        value = value.Replace(',', '.');
        return decimal.TryParse(value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    /// <summary>The number as it is stored: a decimal point and no trailing zeros ("3.14", "-2", "0.5").</summary>
    public static string Format(decimal number)
        => number.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>A stored number (or "*") as it is shown: with a decimal comma ("3,14").</summary>
    public static string Display(string? stored)
        => stored == AnyNumber ? "всяко число" : (stored ?? string.Empty).Replace('.', ',');

    /// <summary>A tolerance as it is shown: with a decimal comma and no trailing zeros ("0,01").</summary>
    public static string Display(decimal number) => Format(number).Replace('.', ',');
}
