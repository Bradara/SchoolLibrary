using System.Text.Json;
using System.Text.RegularExpressions;

namespace SchoolLibrary.Services.Discovery;

/// <summary>
/// Searches the Internet Archive (books and videos) with its public advanced-search API. Only items that
/// carry a Creative Commons or public domain license are kept. The license is declared by whoever uploaded
/// the item, so the suggestions still need a teacher's review before they are saved.
/// </summary>
public class InternetArchiveSourceProvider : IResourceSourceProvider
{
    public const string HttpClientName = "internetarchive";
    public const string DefaultAuthor = "Internet Archive";
    private const int MaxDescriptionLength = 300;

    private static readonly Regex Identifier = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

    // CC license addresses (any http/https spelling) mapped to the names of the licenses the platform has.
    // A license that is not in the list (for example "no derivatives, non-commercial") is not offered.
    private static readonly (Regex Pattern, string Name)[] Licenses =
    {
        (new Regex(@"creativecommons\.org/publicdomain/zero/1\.0", RegexOptions.IgnoreCase | RegexOptions.Compiled), "CC0 1.0"),
        (new Regex(@"creativecommons\.org/publicdomain/mark/1\.0", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Обществено достояние"),
        (new Regex(@"creativecommons\.org/licenses/by/4\.0", RegexOptions.IgnoreCase | RegexOptions.Compiled), "CC BY 4.0"),
        (new Regex(@"creativecommons\.org/licenses/by/3\.0", RegexOptions.IgnoreCase | RegexOptions.Compiled), "CC BY 3.0"),
        (new Regex(@"creativecommons\.org/licenses/by-sa/4\.0", RegexOptions.IgnoreCase | RegexOptions.Compiled), "CC BY-SA 4.0"),
        (new Regex(@"creativecommons\.org/licenses/by-nc/4\.0", RegexOptions.IgnoreCase | RegexOptions.Compiled), "CC BY-NC 4.0"),
        (new Regex(@"creativecommons\.org/licenses/by-nc-sa/4\.0", RegexOptions.IgnoreCase | RegexOptions.Compiled), "CC BY-NC-SA 4.0")
    };

    private readonly IHttpClientFactory _httpFactory;

    public InternetArchiveSourceProvider(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public string Key => "internet-archive";

    public string DisplayName => "Internet Archive (книги и видео)";

    /// <summary>The name of the platform license for an Internet Archive license address, or null when there is none.</summary>
    public static string? LicenseNameFor(string? licenseUrl) =>
        string.IsNullOrWhiteSpace(licenseUrl) ? null : Licenses.FirstOrDefault(l => l.Pattern.IsMatch(licenseUrl)).Name;

    public async Task<IReadOnlyList<ResourceCandidate>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var terms = SearchTerms.Extract(query);
        if (terms.Count == 0) return Array.Empty<ResourceCandidate>();

        // The archive does not know word endings, so every word is searched by its beginning ("древ*").
        // Items that have every word come first; when there are none, items that have at least half of them.
        var found = await QueryAsync(terms, "AND", maxResults, cancellationToken);
        if (found.Count == 0 && terms.Count > 1)
        {
            var required = SearchTerms.RelaxedRequirement(terms.Count);
            found = (await QueryAsync(terms, "OR", maxResults * 3, cancellationToken))
                .Select(c => (Candidate: c, Matches: SearchTerms.CountMatches(c.Title, terms)))
                .Where(x => x.Matches >= required)
                .OrderByDescending(x => x.Matches)
                .Take(maxResults)
                .Select(x => x.Candidate)
                .ToList();
        }

        return found;
    }

    private async Task<IReadOnlyList<ResourceCandidate>> QueryAsync(IReadOnlyList<string> terms, string joiner, int maxResults, CancellationToken cancellationToken)
    {
        // More rows than needed: some items are dropped because their license is not one the platform knows
        var url = "https://archive.org/advancedsearch.php?output=json&sort%5B%5D=downloads+desc" +
                  $"&rows={maxResults * 3}" +
                  "&fl%5B%5D=identifier&fl%5B%5D=title&fl%5B%5D=creator&fl%5B%5D=description&fl%5B%5D=licenseurl" +
                  $"&q={Uri.EscapeDataString(BuildQuery(terms, joiner))}";

        using var http = _httpFactory.CreateClient(HttpClientName);
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

        var candidates = new List<ResourceCandidate>();
        if (!document.RootElement.TryGetProperty("response", out var responseElement)
            || !responseElement.TryGetProperty("docs", out var docs))
        {
            return candidates;
        }

        foreach (var doc in docs.EnumerateArray())
        {
            var identifier = Text(doc, "identifier");
            var title = Text(doc, "title");
            var license = LicenseNameFor(Text(doc, "licenseurl"));
            if (identifier == null || !Identifier.IsMatch(identifier) || string.IsNullOrWhiteSpace(title) || license == null) continue;

            var creator = DiscoveryText.Clean(Text(doc, "creator"), 100);
            candidates.Add(new ResourceCandidate(
                Title: Truncate(DiscoveryText.Clean(title, int.MaxValue), 100),
                Url: $"https://archive.org/details/{identifier}",
                Author: creator.Length > 0 ? creator : DefaultAuthor,
                SourceName: DisplayName,
                Description: DiscoveryText.Clean(Text(doc, "description"), MaxDescriptionLength),
                LicenseName: license));

            if (candidates.Count == maxResults) break;
        }

        return candidates;
    }

    // Only the words of the topic go into the query (letters and digits, see SearchTerms), never the raw text, so a user
    // cannot add search fields or operators. Words must be in the title; books and videos only; CC licenses only.
    private static string BuildQuery(IReadOnlyList<string> terms, string joiner) =>
        $"title:({string.Join($" {joiner} ", terms.Select(t => SearchTerms.Stem(t) + "*"))}) AND mediatype:(texts OR movies) AND licenseurl:*creativecommons*";

    // The API returns a string, or a list of strings when the field was set more than once
    private static string? Text(JsonElement doc, string name)
    {
        if (!doc.TryGetProperty(name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString())),
            _ => null
        };
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();
}
