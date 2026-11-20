using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SchoolLibrary.Services.SiteIcons;

/// <summary>An icon downloaded from a site: the bytes and the image type recognized from them.</summary>
public record SiteIcon(byte[] Content, string ContentType);

/// <summary>Downloads the icon of a web site (apple-touch-icon, icon from the page, or /favicon.ico).</summary>
public interface ISiteIconFetcher
{
    /// <summary>The best icon the site offers, or null when none could be downloaded or recognized.</summary>
    Task<SiteIcon?> FetchAsync(string siteUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the start page of a site, picks the largest icon it declares and checks the downloaded bytes
/// against the image signatures. SVG is refused: opened on its own it can run script on this site.
/// The HTTP client is created with <see cref="PublicNetworkGuard"/>, so only public addresses are reached.
/// </summary>
public partial class SiteIconFetcher : ISiteIconFetcher
{
    public const string HttpClientName = "site-icons";

    /// <summary>Only the beginning of the page is read: the icon links are in the head.</summary>
    public const int MaxPageBytes = 256 * 1024;

    public const int MaxIconBytes = 200 * 1024;

    /// <summary>The page and at most this many icon addresses are tried.</summary>
    public const int MaxIconAttempts = 4;

    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(20);

    private readonly IHttpClientFactory _httpFactory;

    public SiteIconFetcher(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public async Task<SiteIcon?> FetchAsync(string siteUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(siteUrl?.Trim(), UriKind.Absolute, out var site) || !IsWebAddress(site))
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TotalTimeout);
        var token = timeout.Token;

        using var http = _httpFactory.CreateClient(HttpClientName);

        try
        {
            var (html, pageUri) = await ReadPageAsync(http, site, token);

            var candidates = (html == null ? Enumerable.Empty<Uri>() : FindIconLinks(html, pageUri))
                .Append(new Uri(pageUri, "/favicon.ico"))
                .Distinct()
                .Take(MaxIconAttempts);

            foreach (var candidate in candidates)
            {
                var icon = await TryDownloadIconAsync(http, candidate, token);
                if (icon != null) return icon;
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The site was too slow: the caller keeps the chosen Bootstrap icon
            return null;
        }
    }

    /// <summary>
    /// The icon addresses the page declares, best first: apple-touch-icon (usually 180×180) and then the
    /// icons by their declared size. SVG icons and non-web addresses are left out.
    /// </summary>
    public static IReadOnlyList<Uri> FindIconLinks(string html, Uri pageUri)
    {
        var found = new List<(Uri Uri, int Score)>();

        foreach (Match tag in LinkTagRegex().Matches(html))
        {
            var attributes = ParseAttributes(tag.Value);
            if (!attributes.TryGetValue("rel", out var rel) || !attributes.TryGetValue("href", out var href)) continue;

            var relTokens = rel.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var isTouch = relTokens.Any(t => t.StartsWith("apple-touch-icon"));
            if (!isTouch && !relTokens.Contains("icon")) continue;

            attributes.TryGetValue("type", out var type);
            if (type != null && type.Contains("svg", StringComparison.OrdinalIgnoreCase)) continue;

            if (!Uri.TryCreate(pageUri, WebUtility.HtmlDecode(href).Trim(), out var uri) || !IsWebAddress(uri)) continue;
            if (uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;

            attributes.TryGetValue("sizes", out var sizes);
            var size = LargestSize(sizes) ?? (isTouch ? 180 : 16);
            found.Add((uri, size));
        }

        return found
            .OrderByDescending(f => f.Score)
            .Select(f => f.Uri)
            .Distinct()
            .ToList();
    }

    /// <summary>The image type recognized from the first bytes, or null for anything else (SVG, HTML, ...).</summary>
    public static string? DetectImageType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return "image/gif";
        if (bytes.StartsWith(new byte[] { 0x00, 0x00, 0x01, 0x00 })) return "image/x-icon";
        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    private static async Task<(string? Html, Uri PageUri)> ReadPageAsync(HttpClient http, Uri site, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, site);
            request.Headers.Accept.ParseAdd("text/html");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

            // After redirects the icons are relative to the final address
            var pageUri = response.RequestMessage?.RequestUri ?? site;
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode || (mediaType != null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)))
            {
                return (null, pageUri);
            }

            var bytes = await ReadLimitedAsync(response, MaxPageBytes, truncate: true, token);
            // The link tags are ASCII, so the page encoding does not matter for them
            return (bytes == null ? null : Encoding.UTF8.GetString(bytes), pageUri);
        }
        catch (HttpRequestException)
        {
            // /favicon.ico may still work
            return (null, site);
        }
    }

    private static async Task<SiteIcon?> TryDownloadIconAsync(HttpClient http, Uri uri, CancellationToken token)
    {
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await ReadLimitedAsync(response, MaxIconBytes, truncate: false, token);
            if (bytes == null || bytes.Length == 0) return null;

            var type = DetectImageType(bytes);
            return type == null ? null : new SiteIcon(bytes, type);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // Reads at most "limit" bytes. A longer body is cut (the page) or refused (an icon) instead of being read whole.
    private static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, int limit, bool truncate, CancellationToken token)
    {
        if (!truncate && response.Content.Headers.ContentLength > limit) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[limit + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), token)) > 0)
        {
            total += read;
        }

        if (total > limit)
        {
            if (!truncate) return null;
            total = limit;
        }

        return buffer[..total];
    }

    private static bool IsWebAddress(Uri uri) =>
        uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static Dictionary<string, string> ParseAttributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in AttributeRegex().Matches(tag))
        {
            attributes.TryAdd(attribute.Groups["name"].Value, attribute.Groups["value"].Value);
        }

        return attributes;
    }

    // "32x32 192x192" -> 192; "any" or nothing -> null
    private static int? LargestSize(string? sizes)
    {
        if (string.IsNullOrWhiteSpace(sizes)) return null;

        var values = sizes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.ToLowerInvariant().Split('x'))
            .Where(p => p.Length == 2 && int.TryParse(p[0], out _))
            .Select(p => int.Parse(p[0]))
            .ToList();

        return values.Count == 0 ? null : values.Max();
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinkTagRegex();

    [GeneratedRegex(@"(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s""'>]+))", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AttributeRegex();
}
