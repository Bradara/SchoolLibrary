using System.Text.Json;

namespace SchoolLibrary.Services.Discovery;

/// <summary>A Wikimedia site that can be searched, with the author line used for its pages.</summary>
public record WikimediaWiki(string Key, string Host, string DisplayName, string Author);

/// <summary>
/// Searches one Wikimedia site (Wikipedia, Wikibooks, Wikisource) with the public MediaWiki search API.
/// Their text is published under CC BY-SA, so every candidate gets that license.
/// </summary>
public class WikimediaSourceProvider : IResourceSourceProvider
{
    public const string HttpClientName = "wikimedia";
    public const string LicenseName = "CC BY-SA 4.0";
    private const int MaxDescriptionLength = 300;

    /// <summary>The sites the search can use. Hosts are fixed here, never taken from user input.</summary>
    public static readonly IReadOnlyList<WikimediaWiki> Wikis = new[]
    {
        new WikimediaWiki("wikipedia-bg", "bg.wikipedia.org", "Уикипедия (български)", "Сътрудници на Уикипедия"),
        new WikimediaWiki("wikibooks-bg", "bg.wikibooks.org", "Уикикниги (български)", "Сътрудници на Уикикниги"),
        new WikimediaWiki("wikisource-bg", "bg.wikisource.org", "Уикиизточник (български)", "Сътрудници на Уикиизточник"),
        new WikimediaWiki("wikipedia-ru", "ru.wikipedia.org", "Уикипедия (руски)", "Участники Википедии"),
        new WikimediaWiki("wikipedia-simple", "simple.wikipedia.org", "Уикипедия (опростен английски)", "Wikipedia contributors")
    };

    private readonly HttpClient _http;
    private readonly WikimediaWiki _wiki;

    public WikimediaSourceProvider(HttpClient http, WikimediaWiki wiki)
    {
        _http = http;
        _wiki = wiki;
    }

    public string Key => _wiki.Key;

    public string DisplayName => _wiki.DisplayName;

    public async Task<IReadOnlyList<ResourceCandidate>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var url = $"https://{_wiki.Host}/w/api.php?action=query&list=search&format=json&utf8=1&srprop=snippet" +
                  $"&srnamespace=0&srlimit={maxResults}&srsearch={Uri.EscapeDataString(query)}";

        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

        var candidates = new List<ResourceCandidate>();
        if (!document.RootElement.TryGetProperty("query", out var queryElement)
            || !queryElement.TryGetProperty("search", out var hits))
        {
            return candidates;
        }

        foreach (var hit in hits.EnumerateArray())
        {
            var title = hit.TryGetProperty("title", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) continue;

            var snippet = hit.TryGetProperty("snippet", out var s) ? s.GetString() : null;
            candidates.Add(new ResourceCandidate(
                Title: title,
                Url: PageUrl(title),
                Author: _wiki.Author,
                SourceName: _wiki.DisplayName,
                Description: DiscoveryText.Clean(snippet, MaxDescriptionLength),
                LicenseName: LicenseName));
        }

        return candidates;
    }

    private string PageUrl(string title) => $"https://{_wiki.Host}/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}";
}
