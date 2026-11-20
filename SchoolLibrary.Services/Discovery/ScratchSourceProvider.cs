using System.Globalization;
using System.Text.Json;

namespace SchoolLibrary.Services.Discovery;

/// <summary>
/// Searches the shared projects on Scratch (block programming) with its public API. The API cannot filter by
/// language, so the Bulgarian titles are put first. Projects shared under the Terms of Use that license them as
/// CC BY-SA 2.0 are the only ones suggested: the terms were reported to change in January 2026, and a project
/// shared before that keeps its license (a Creative Commons license cannot be taken back).
/// </summary>
public class ScratchSourceProvider : IResourceSourceProvider
{
    public const string HttpClientName = "scratch";
    public const string LicenseName = "CC BY-SA 2.0";
    private const int MaxDescriptionLength = 300;

    /// <summary>Projects shared on or after this day are not suggested (see the class summary).</summary>
    public static readonly DateTimeOffset SharedBefore = new(2026, 1, 22, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Projects seen fewer times are not suggested. Most of them are homework shared by a class (often with a
    /// child's name in the title or the user name): they are rarely useful to another teacher.
    /// </summary>
    public const long MinViews = 20;

    /// <summary>More projects are asked for than shown: some are dropped by the date and the checks.</summary>
    public const int RequestedProjects = 40;

    private readonly IHttpClientFactory _httpFactory;

    public ScratchSourceProvider(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public string Key => "scratch";

    public string DisplayName => "Scratch (проекти с блоково програмиране)";

    public async Task<IReadOnlyList<ResourceCandidate>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var terms = SearchTerms.Extract(query);
        if (terms.Count == 0) return Array.Empty<ResourceCandidate>();

        // Only the extracted words are sent, escaped: never the raw text
        var url = $"https://api.scratch.mit.edu/search/projects?mode=popular&limit={RequestedProjects}&q={Uri.EscapeDataString(string.Join(' ', terms))}";

        using var http = _httpFactory.CreateClient(HttpClientName);
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<ResourceCandidate>();

        var found = new List<(ResourceCandidate Candidate, bool Bulgarian, int TitleMatches, long Loves)>();
        foreach (var project in document.RootElement.EnumerateArray())
        {
            // TryGetInt64 throws on a string, so the kind is checked first
            if (project.ValueKind != JsonValueKind.Object
                || !project.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.Number
                || !idElement.TryGetInt64(out var id) || id <= 0) continue;

            var title = DiscoveryText.Clean(Text(project, "title"), 100);
            if (title.Length == 0 || !SharedInTime(project) || Stat(project, "views") < MinViews) continue;

            var instructions = Text(project, "instructions");
            var notes = Text(project, "description");
            var description = DiscoveryText.Clean(string.IsNullOrWhiteSpace(instructions) ? notes : instructions, MaxDescriptionLength);

            // The search also looks at the project text; at least one of the words must be in the title or that text
            if (SearchTerms.CountMatches($"{title} {instructions} {notes}", terms) == 0) continue;

            var author = project.TryGetProperty("author", out var authorElement) ? DiscoveryText.Clean(Text(authorElement, "username"), 100) : string.Empty;

            var candidate = new ResourceCandidate(
                Title: title,
                Url: $"https://scratch.mit.edu/projects/{id}/",
                Author: author.Length > 0 ? author : "Scratch",
                SourceName: DisplayName,
                Description: description,
                LicenseName: LicenseName);

            found.Add((candidate, BulgarianText.LooksBulgarian(title), SearchTerms.CountMatches(title, terms), Stat(project, "loves")));
        }

        return found
            .OrderByDescending(f => f.Bulgarian)
            .ThenByDescending(f => f.TitleMatches)
            .ThenByDescending(f => f.Loves)
            .Take(maxResults)
            .Select(f => f.Candidate)
            .ToList();
    }

    // A project without a readable sharing date is not suggested: its license cannot be told
    private static bool SharedInTime(JsonElement project) =>
        project.TryGetProperty("history", out var history)
        && DateTimeOffset.TryParse(Text(history, "shared"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var shared)
        && shared < SharedBefore;

    // A missing or unreadable number counts as 0
    private static long Stat(JsonElement project, string name) =>
        project.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object
        && stats.TryGetProperty(name, out var stat) && stat.ValueKind == JsonValueKind.Number && stat.TryGetInt64(out var value)
            ? value
            : 0;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
