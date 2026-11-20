using System.Text.Json;
using System.Text.RegularExpressions;

namespace SchoolLibrary.Services.Discovery;

/// <summary>
/// Searches the materials teachers publish on GeoGebra (interactive activities, books and applets, mostly
/// mathematics) with its public search API. The API cannot filter by language, so the Bulgarian titles are put
/// first. Copies of other people's materials ("Копие на ...") are left out: the original is the one to link.
/// Everything on GeoGebra is used under the GeoGebra license, which allows non-commercial use in school.
/// </summary>
public class GeoGebraSourceProvider : IResourceSourceProvider
{
    public const string HttpClientName = "geogebra";
    public const string LicenseName = "Лиценз на GeoGebra";
    public const string DefaultAuthor = "GeoGebra";

    private const string SearchUrl = "https://api.geogebra.org/v1.0/search/materials?query=";

    // Material ids are short letters and digits (for example "BYGyGD6E")
    private static readonly Regex MaterialId = new("^[A-Za-z0-9]{4,16}$", RegexOptions.Compiled);

    private static readonly Regex CopyTitle = new(@"^\s*(Копие на|Копия|Копія|Copy of)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpFactory;

    public GeoGebraSourceProvider(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public string Key => "geogebra";

    public string DisplayName => "GeoGebra (интерактивни материали)";

    public async Task<IReadOnlyList<ResourceCandidate>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var terms = SearchTerms.Extract(query);
        if (terms.Count == 0) return Array.Empty<ResourceCandidate>();

        // Only the extracted words are sent, joined by spaces and escaped: never the raw text
        using var http = _httpFactory.CreateClient(HttpClientName);
        using var response = await http.GetAsync(SearchUrl + Uri.EscapeDataString(string.Join(' ', terms)), cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ResourceCandidate>();
        }

        var found = new List<(ResourceCandidate Candidate, int Matches, bool Bulgarian)>();
        foreach (var hit in hits.EnumerateArray())
        {
            var id = Text(hit, "id");
            var title = DiscoveryText.Clean(Text(hit, "title"), 100);
            if (id == null || !MaterialId.IsMatch(id) || title.Length == 0 || CopyTitle.IsMatch(title)) continue;

            // "O" is a public material; shared-by-link and private ones are not suggested
            if (Text(hit, "visibility") is { } visibility && visibility != "O") continue;

            var author = hit.TryGetProperty("creator", out var creator) ? DiscoveryText.Clean(Text(creator, "name"), 100) : string.Empty;

            var candidate = new ResourceCandidate(
                Title: title,
                Url: $"https://www.geogebra.org/m/{id}",
                Author: author.Length > 0 ? author : DefaultAuthor,
                SourceName: DisplayName,
                Description: $"{KindName(Text(hit, "type"))} в GeoGebra.",
                LicenseName: LicenseName);

            found.Add((candidate, SearchTerms.CountMatches(title, terms), BulgarianText.LooksBulgarian(title)));
        }

        // The search also matches descriptions and tags, so a title with more of the words is the better answer
        return found
            .OrderByDescending(f => f.Bulgarian)
            .ThenByDescending(f => f.Matches)
            .Take(maxResults)
            .Select(f => f.Candidate)
            .ToList();
    }

    private static string KindName(string? type) => type switch
    {
        "ws" => "Интерактивна дейност",
        "book" => "Сборник с дейности",
        "ggb" => "Аплет",
        _ => "Материал"
    };

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
