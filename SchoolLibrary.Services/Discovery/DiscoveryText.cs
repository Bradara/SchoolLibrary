using System.Net;
using System.Text.RegularExpressions;

namespace SchoolLibrary.Services.Discovery;

/// <summary>Cleans text that comes from outside sources before it is shown or saved.</summary>
internal static class DiscoveryText
{
    private static readonly Regex HtmlTag = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Plain text: tags removed, entities decoded, white space collapsed, cut to <paramref name="maxLength"/> characters (plus an ellipsis).</summary>
    public static string Clean(string? html, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var text = Spaces.Replace(WebUtility.HtmlDecode(HtmlTag.Replace(html, " ")), " ").Trim();
        return text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "…";
    }
}
