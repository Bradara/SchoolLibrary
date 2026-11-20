using System.Text.RegularExpressions;

namespace SchoolLibrary.Services.Discovery;

/// <summary>
/// Tells Bulgarian text from Russian and Ukrainian by the letters only those languages have. Sources such as
/// GeoGebra and Scratch cannot filter by language, so their Bulgarian results are put first with this.
/// </summary>
public static class BulgarianText
{
    private static readonly Regex Cyrillic = new(@"\p{IsCyrillic}", RegexOptions.Compiled);

    // ы э ё (Russian), ї і є ґ (Ukrainian), ў (Belarusian), ј љ њ ћ ђ џ (Serbian, Macedonian)
    private static readonly Regex OtherCyrillic = new("[ыэёїієґўјљњћђџЫЭЁЇІЄҐЎЈЉЊЋЂЏ]", RegexOptions.Compiled);

    /// <summary>True for Cyrillic text without letters that Bulgarian does not use.</summary>
    public static bool LooksBulgarian(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Cyrillic.IsMatch(text) && !OtherCyrillic.IsMatch(text);
}
