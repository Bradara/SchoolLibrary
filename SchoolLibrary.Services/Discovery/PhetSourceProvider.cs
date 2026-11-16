using System.Text.Json;

namespace SchoolLibrary.Services.Discovery;

/// <summary>
/// Searches the interactive simulations of PhET (University of Colorado Boulder), translated into Bulgarian.
/// PhET has no search service, so its public catalog (about 100 simulations) is downloaded once and kept in
/// memory for a few hours; the topic is matched against the Bulgarian title, the learning goals and the English name.
/// Every simulation is published under CC BY-NC 4.0 (the code is GPL).
/// </summary>
public class PhetSourceProvider : IResourceSourceProvider
{
    public const string HttpClientName = "phet";
    public const string LicenseName = "CC BY-NC 4.0";
    public const string Author = "PhET Interactive Simulations, University of Colorado Boulder";

    private const string Host = "https://phet.colorado.edu";
    private const string CatalogUrl = Host + "/services/metadata/1.3/simulations?format=json&type=html&locale=bg";
    private const string Locale = "bg";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

    private readonly IHttpClientFactory _httpFactory;
    private readonly SemaphoreSlim _loading = new(1, 1);
    private List<Simulation>? _catalog;
    private DateTime _loadedAt;

    public PhetSourceProvider(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public string Key => "phet";

    public string DisplayName => "PhET (интерактивни симулации)";

    public async Task<IReadOnlyList<ResourceCandidate>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return Array.Empty<ResourceCandidate>();

        var catalog = await GetCatalogAsync(cancellationToken);

        return catalog
            .Where(s => words.All(w => s.SearchText.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            // A match in the title is a better answer than a match in the learning goals
            .OrderByDescending(s => words.All(w => s.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            .ThenBy(s => s.Title, StringComparer.CurrentCulture)
            .Take(maxResults)
            .Select(s => new ResourceCandidate(
                Title: "Симулация: " + s.Title,
                Url: s.Url,
                Author: Author,
                SourceName: DisplayName,
                Description: s.Description,
                LicenseName: LicenseName))
            .ToList();
    }

    private async Task<List<Simulation>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        if (_catalog != null && DateTime.UtcNow - _loadedAt < CacheLifetime) return _catalog;

        await _loading.WaitAsync(cancellationToken);
        try
        {
            if (_catalog != null && DateTime.UtcNow - _loadedAt < CacheLifetime) return _catalog;

            using var http = _httpFactory.CreateClient(HttpClientName);
            using var response = await http.GetAsync(CatalogUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

            _catalog = Parse(document.RootElement);
            _loadedAt = DateTime.UtcNow;
            return _catalog;
        }
        finally
        {
            _loading.Release();
        }
    }

    private static List<Simulation> Parse(JsonElement root)
    {
        var simulations = new List<Simulation>();
        if (!root.TryGetProperty("projects", out var projects)) return simulations;

        foreach (var project in projects.EnumerateArray())
        {
            if (!project.TryGetProperty("simulations", out var items)) continue;

            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("localizedSimulations", out var localized)
                    || !localized.TryGetProperty(Locale, out var bg)) continue;

                var title = Text(bg, "title");
                var runUrl = Text(bg, "runUrl");
                // Only a path on PhET itself becomes a link; anything else in the catalog is ignored
                if (string.IsNullOrWhiteSpace(title) || runUrl == null || !runUrl.StartsWith("/sims/", StringComparison.Ordinal)) continue;

                var goals = Text(bg, "learningGoals") ?? string.Empty;
                var englishName = (Text(item, "name") ?? string.Empty).Replace('-', ' ');
                simulations.Add(new Simulation(
                    title.Trim(),
                    Host + runUrl,
                    string.IsNullOrWhiteSpace(goals) ? "Интерактивна симулация на PhET." : goals.Trim(),
                    $"{title} {goals} {englishName}"));
            }
        }

        return simulations;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record Simulation(string Title, string Url, string Description, string SearchText);
}
