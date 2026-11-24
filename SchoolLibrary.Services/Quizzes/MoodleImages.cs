using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Reads the pictures of a Moodle XML question text. Moodle writes a picture as
/// <c>&lt;img src="@@PLUGINFILE@@/name.png"&gt;</c> in the HTML and the bytes as
/// <c>&lt;file name="name.png" path="/" encoding="base64"&gt;</c> next to the text; a picture may also be
/// a <c>data:</c> address. Each accepted picture becomes an <c>[[img:N]]</c> marker in the HTML; the rest
/// (addresses on other sites, missing or broken files, other types, too large, over the limit) are
/// removed with a reason. Pictures from other sites are never downloaded.
/// </summary>
public static partial class MoodleImages
{
    private const string PluginFilePrefix = "@@PLUGINFILE@@";

    /// <summary>
    /// The HTML of the question text with the pictures replaced by markers, and the pictures. Reasons
    /// for skipped pictures are added to <paramref name="errors"/>, starting with <paramref name="label"/>.
    /// </summary>
    public static (string Html, IReadOnlyList<ImportedImage> Images) Extract(XElement? questionText, string label, List<string> errors)
    {
        var html = questionText?.Element("text")?.Value ?? string.Empty;
        var images = new List<ImportedImage>();
        if (!html.Contains("<img", StringComparison.OrdinalIgnoreCase))
        {
            return (html, images);
        }

        var files = (questionText?.Elements("file") ?? Enumerable.Empty<XElement>())
            .Where(f => string.Equals((string?)f.Attribute("encoding"), "base64", StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => FilePath((string?)f.Attribute("path"), (string?)f.Attribute("name")), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

        // The same picture used twice keeps one number
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);

        var result = ImgTagRegex().Replace(html, match =>
        {
            var tag = match.Value;
            var src = WebUtility.HtmlDecode(Attribute(tag, "src") ?? string.Empty).Trim();
            if (numbers.TryGetValue(src, out var known))
            {
                return $" {QuestionImageMarkers.Marker(known)} ";
            }

            var (name, bytes, problem) = Read(src, files);
            if (problem == null && images.Count >= QuestionImage.MaxPerQuestion)
            {
                problem = $"един въпрос може да има най-много {QuestionImage.MaxPerQuestion} изображения";
            }

            var type = bytes == null ? null : QuestionImageFiles.DetectType(bytes);
            if (problem == null && bytes!.Length > QuestionImage.MaxSizeBytes)
            {
                problem = $"по-голямо е от {QuestionImage.MaxSizeBytes / (1024 * 1024)} МБ";
            }
            else if (problem == null && type == null)
            {
                problem = "разрешени са само PNG, JPEG, GIF и WebP";
            }

            if (problem != null)
            {
                errors.Add($"{label}: изображението „{Shorten(name)}“ е пропуснато — {problem}.");
                return " ";
            }

            var number = images.Count + 1;
            numbers[src] = number;
            images.Add(new ImportedImage(number, QuestionImageFiles.CleanFileName(name, type!.Value.Extension),
                bytes!, type.Value.ContentType, AltText(tag)));
            return $" {QuestionImageMarkers.Marker(number)} ";
        });

        return (result, images);
    }

    // The bytes behind a src, or the reason they cannot be read
    private static (string Name, byte[]? Bytes, string? Problem) Read(string src, IReadOnlyDictionary<string, string> files)
    {
        if (src.StartsWith(PluginFilePrefix, StringComparison.Ordinal))
        {
            var path = Unescape(src[PluginFilePrefix.Length..].Split('?', '#')[0]);
            var name = path[(path.LastIndexOf('/') + 1)..];
            if (!files.TryGetValue(path, out var encoded))
            {
                return (name, null, "файлът липсва във файла за внос");
            }

            var bytes = FromBase64(encoded);
            return bytes == null ? (name, null, "файлът е повреден") : (name, bytes, null);
        }

        if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = src.IndexOf(',');
            if (comma < 0 || !src[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            {
                return ("image", null, "форматът на адреса не се поддържа");
            }

            var bytes = FromBase64(src[(comma + 1)..]);
            return bytes == null ? ("image", null, "файлът е повреден") : ("image", bytes, null);
        }

        var shown = src.Length == 0 ? "image" : src;
        return (shown, null, "изображения от други сайтове не се внасят — качете файла");
    }

    // Moodle's file key: the folder ("/" or "/sub/") and the name, as "/sub/name.png"
    private static string FilePath(string? path, string? name)
    {
        var folder = string.IsNullOrEmpty(path) ? "/" : path;
        if (!folder.StartsWith('/')) folder = "/" + folder;
        if (!folder.EndsWith('/')) folder += "/";
        return folder + (name ?? string.Empty);
    }

    private static string Unescape(string path)
    {
        try
        {
            return Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return path;
        }
    }

    private static byte[]? FromBase64(string encoded)
    {
        var clean = WhitespaceRegex().Replace(encoded, string.Empty);
        if (clean.Length == 0)
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(clean);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? AltText(string tag)
    {
        var alt = Attribute(tag, "alt");
        if (alt == null)
        {
            return null;
        }

        var text = WhitespaceRegex().Replace(WebUtility.HtmlDecode(alt), " ").Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length > QuestionImage.AltTextMaxLength ? text[..QuestionImage.AltTextMaxLength].TrimEnd() : text;
    }

    private static string? Attribute(string tag, string name)
    {
        foreach (Match m in AttributeRegex().Matches(tag))
        {
            if (string.Equals(m.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
            {
                return m.Groups["dq"].Success ? m.Groups["dq"].Value
                    : m.Groups["sq"].Success ? m.Groups["sq"].Value
                    : m.Groups["bare"].Value;
            }
        }

        return null;
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..60] + "…";

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTagRegex();

    [GeneratedRegex(@"(?<name>[a-zA-Z-]+)\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s""'>]+))")]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
