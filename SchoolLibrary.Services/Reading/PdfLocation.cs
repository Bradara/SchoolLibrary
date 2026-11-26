using System.Globalization;
using System.Text.RegularExpressions;

namespace SchoolLibrary.Services.Reading;

/// <summary>
/// A place in an uploaded PDF, kept in the same column as an EPUB location: "pdfpage(12)" is page 12.
/// </summary>
public static partial class PdfLocation
{
    public const string PdfContentType = "application/pdf";

    /// <summary>More pages than any real document has; a bigger number is refused.</summary>
    public const int MaxPage = 100_000;

    [GeneratedRegex(@"^pdfpage\(([1-9][0-9]{0,5})\)$", RegexOptions.CultureInvariant)]
    private static partial Regex LocationPattern();

    /// <summary>The location of the page, for example "pdfpage(12)".</summary>
    public static string Format(int page) => "pdfpage(" + page.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>The page number of a location such as "pdfpage(12)", or null when it is not one.</summary>
    public static int? PageOf(string? location)
    {
        if (string.IsNullOrEmpty(location))
        {
            return null;
        }

        var match = LocationPattern().Match(location);
        if (!match.Success)
        {
            return null;
        }

        var page = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return page <= MaxPage ? page : null;
    }

    public static bool IsValid(string? location) => PageOf(location) != null;
}
