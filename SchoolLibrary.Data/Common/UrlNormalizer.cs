using System.Text.RegularExpressions;

namespace SchoolLibrary.Data.Common;

/// <summary>
/// Turns a resource link into one canonical form, so that two spellings of the same link
/// (http/https, www, tracking parameters, YouTube and Google Drive variants) are found equal.
/// </summary>
public static class UrlNormalizer
{
    /// <summary>Longest possible result; the database column has the same length.</summary>
    public const int MaxLength = 600;

    private static readonly string[] YouTubeHosts =
    {
        "youtube.com", "m.youtube.com", "music.youtube.com", "youtube-nocookie.com", "youtu.be"
    };

    private static readonly Regex DriveFile = new(@"^/file/d/([\w-]+)", RegexOptions.Compiled);
    private static readonly Regex DocsFile = new(@"^/(document|presentation|spreadsheets)/d/([\w-]+)", RegexOptions.Compiled);
    private static readonly Regex YouTubePath = new(@"^/(?:embed|shorts|live|v)/([\w-]{6,})", RegexOptions.Compiled);

    /// <summary>The canonical form of an http(s) link, or null when the text is not such a link.</summary>
    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www."))
        {
            host = host[4..];
        }

        var special = NormalizeKnownSite(host, uri);
        if (special != null)
        {
            return special;
        }

        var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : string.Empty;
        var query = CleanQuery(uri.Query);
        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port;

        return $"https://{host}{port}{path}{query}";
    }

    private static string? NormalizeKnownSite(string host, Uri uri)
    {
        if (YouTubeHosts.Contains(host))
        {
            var id = GetYouTubeId(host, uri);
            return id == null ? null : $"https://youtube.com/watch?v={id}";
        }

        if (host == "drive.google.com")
        {
            var match = DriveFile.Match(uri.AbsolutePath);
            if (match.Success)
            {
                return $"https://drive.google.com/file/d/{match.Groups[1].Value}";
            }

            // Older links: drive.google.com/open?id=... and drive.google.com/uc?id=...
            var id = GetQueryValue(uri.Query, "id");
            return id == null ? null : $"https://drive.google.com/file/d/{id}";
        }

        if (host == "docs.google.com")
        {
            var match = DocsFile.Match(uri.AbsolutePath);
            if (match.Success)
            {
                return $"https://docs.google.com/{match.Groups[1].Value}/d/{match.Groups[2].Value}";
            }
        }

        return null;
    }

    private static string? GetYouTubeId(string host, Uri uri)
    {
        if (host == "youtu.be")
        {
            var id = uri.AbsolutePath.Trim('/');
            return id.Length > 0 && !id.Contains('/') ? id : null;
        }

        var fromQuery = GetQueryValue(uri.Query, "v");
        if (fromQuery != null)
        {
            return fromQuery;
        }

        var match = YouTubePath.Match(uri.AbsolutePath);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? GetQueryValue(string query, string name)
    {
        foreach (var pair in SplitQuery(query))
        {
            if (pair.Name == name && pair.Value.Length > 0)
            {
                return pair.Value;
            }
        }

        return null;
    }

    // Tracking parameters are dropped; the rest is sorted so that parameter order does not matter
    private static string CleanQuery(string query)
    {
        var kept = SplitQuery(query)
            .Where(p => !IsTrackingParameter(p.Name))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => p.Value.Length > 0 ? $"{p.Name}={p.Value}" : p.Name)
            .ToList();

        return kept.Count == 0 ? string.Empty : "?" + string.Join("&", kept);
    }

    private static bool IsTrackingParameter(string name)
    {
        name = name.ToLowerInvariant();
        return name.StartsWith("utm_") || name is "fbclid" or "gclid" or "msclkid" or "mc_cid" or "mc_eid";
    }

    private static IEnumerable<(string Name, string Value)> SplitQuery(string query)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = part.IndexOf('=');
            yield return index < 0 ? (part, string.Empty) : (part[..index], part[(index + 1)..]);
        }
    }
}
