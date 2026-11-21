using System.Globalization;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Formatting of quiz points, which can be fractional when a question earns partial credit.</summary>
public static class QuizPoints
{
    /// <summary>
    /// Formats points for display: whole numbers without decimals ("5"), otherwise up to two decimals
    /// with a decimal comma ("2,5", "3,33"). The comma is fixed, so the output does not depend on the
    /// server culture (the container runs with invariant culture).
    /// </summary>
    public static string Format(decimal points)
        => Math.Round(points, 2, MidpointRounding.AwayFromZero)
            .ToString("0.##", CultureInfo.InvariantCulture)
            .Replace('.', ',');
}
