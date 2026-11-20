using System.Text.Json;
using System.Text.RegularExpressions;

namespace SchoolLibrary.Services.Discovery;

/// <summary>
/// Searches YouTube with the YouTube Data API v3. Only videos that may be embedded are asked for, with strict
/// safe search and Bulgarian preferred; the Bulgarian titles are put first. A second call reads each video's
/// license: a video marked Creative Commons gets "CC BY 3.0" (the license YouTube offers), every other one the
/// standard YouTube license, since it is only shown through the YouTube player and never copied.
/// The API key is sent in a header, not in the address, so it does not end up in request logs.
/// </summary>
public class YouTubeSourceProvider : IResourceSourceProvider
{
    public const string HttpClientName = "youtube";
    public const string CreativeCommonsLicenseName = "CC BY 3.0";
    public const string StandardLicenseName = "Стандартен лиценз на YouTube";
    public const int MaxDescriptionLength = 300;

    private const string SearchUrl = "https://www.googleapis.com/youtube/v3/search";
    private const string VideosUrl = "https://www.googleapis.com/youtube/v3/videos";
    private const string ApiKeyHeader = "X-Goog-Api-Key";

    // The API allows 1 to 50 results per page
    private const int MaxPageSize = 50;

    // Video ids are 11 letters, digits, "-" or "_"
    private static readonly Regex VideoId = new("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpFactory;
    private readonly string _apiKey;

    public YouTubeSourceProvider(IHttpClientFactory httpFactory, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("A YouTube API key is required.", nameof(apiKey));

        _httpFactory = httpFactory;
        _apiKey = apiKey.Trim();
    }

    public string Key => "youtube";

    public string DisplayName => "YouTube (видео уроци)";

    public async Task<IReadOnlyList<ResourceCandidate>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var terms = SearchTerms.Extract(query);
        if (terms.Count == 0 || maxResults <= 0) return Array.Empty<ResourceCandidate>();

        using var http = _httpFactory.CreateClient(HttpClientName);

        // Only the extracted words are sent, joined by spaces and escaped: never the raw text
        var searchAddress = SearchUrl
            + "?part=snippet&type=video&videoEmbeddable=true&safeSearch=strict&relevanceLanguage=bg&regionCode=BG"
            + $"&maxResults={Math.Min(maxResults * 2, MaxPageSize)}"
            + "&q=" + Uri.EscapeDataString(string.Join(' ', terms));

        var found = new List<(string Id, string Title, string Author, string Description, int Matches, bool Bulgarian)>();
        using (var search = await GetJsonAsync(http, searchAddress, cancellationToken))
        {
            if (!search.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ResourceCandidate>();
            }

            foreach (var item in items.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idElement) ? Text(idElement, "videoId") : null;
                if (id == null || !VideoId.IsMatch(id) || found.Any(f => f.Id == id)) continue;
                if (!item.TryGetProperty("snippet", out var snippet)) continue;

                // Live and upcoming broadcasts are not lessons
                if (Text(snippet, "liveBroadcastContent") is { } live && live != "none") continue;

                var title = DiscoveryText.Clean(Text(snippet, "title"), 100);
                if (title.Length == 0) continue;

                var author = DiscoveryText.Clean(Text(snippet, "channelTitle"), 100);
                var description = DiscoveryText.Clean(Text(snippet, "description"), MaxDescriptionLength);

                found.Add((id, title, author.Length > 0 ? author : "YouTube", description,
                    SearchTerms.CountMatches(title, terms), BulgarianText.LooksBulgarian(title)));
            }
        }

        if (found.Count == 0) return Array.Empty<ResourceCandidate>();

        var licenses = await ReadLicensesAsync(http, found.Select(f => f.Id), cancellationToken);

        return found
            .Where(f => licenses.ContainsKey(f.Id))
            .OrderByDescending(f => f.Bulgarian)
            .ThenByDescending(f => f.Matches)
            .Take(maxResults)
            .Select(f => new ResourceCandidate(
                Title: f.Title,
                Url: $"https://www.youtube.com/watch?v={f.Id}",
                Author: f.Author,
                SourceName: DisplayName,
                Description: f.Description.Length > 0 ? f.Description : "Видео в YouTube.",
                LicenseName: licenses[f.Id]))
            .ToList();
    }

    // Video id -> license name, only for public videos that can still be embedded
    private async Task<Dictionary<string, string>> ReadLicensesAsync(HttpClient http, IEnumerable<string> ids, CancellationToken cancellationToken)
    {
        var address = VideosUrl + "?part=status&id=" + Uri.EscapeDataString(string.Join(',', ids));
        using var videos = await GetJsonAsync(http, address, cancellationToken);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!videos.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in items.EnumerateArray())
        {
            var id = Text(item, "id");
            if (id == null || !VideoId.IsMatch(id) || !item.TryGetProperty("status", out var status)) continue;
            if (Text(status, "privacyStatus") != "public") continue;
            if (!status.TryGetProperty("embeddable", out var embeddable) || embeddable.ValueKind != JsonValueKind.True) continue;

            result[id] = Text(status, "license") == "creativeCommon" ? CreativeCommonsLicenseName : StandardLicenseName;
        }

        return result;
    }

    private async Task<JsonDocument> GetJsonAsync(HttpClient http, string address, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Add(ApiKeyHeader, _apiKey);

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
