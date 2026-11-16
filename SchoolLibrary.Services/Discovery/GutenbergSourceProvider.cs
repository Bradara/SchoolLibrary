using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SchoolLibrary.Services.Discovery;

/// <summary>
/// Searches Project Gutenberg through its OPDS (Atom) search feed. The books are in the public domain,
/// so every candidate gets that license. The feed also holds navigation entries; only entries that
/// point to a book (/ebooks/{number}) become candidates.
/// </summary>
public class GutenbergSourceProvider : IResourceSourceProvider
{
    public const string HttpClientName = "gutenberg";
    public const string LicenseName = "Обществено достояние";
    private const string Host = "https://www.gutenberg.org";
    private const int MaxDescriptionLength = 300;

    private static readonly Regex BookId = new(@"/ebooks/(\d+)(?:\.opds)?/?$", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpFactory;

    public GutenbergSourceProvider(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    public string Key => "gutenberg";

    public string DisplayName => "Проект Гутенберг (книги)";

    public async Task<IReadOnlyList<ResourceCandidate>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        var url = $"{Host}/ebooks/search.opds/?query={Uri.EscapeDataString(query)}";

        using var http = _httpFactory.CreateClient(HttpClientName);
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await XDocument.LoadAsync(body, LoadOptions.None, cancellationToken);

        var candidates = new List<ResourceCandidate>();
        foreach (var entry in document.Descendants().Where(e => e.Name.LocalName == "entry"))
        {
            var id = Child(entry, "id");
            var title = Child(entry, "title");
            var match = id == null ? null : BookId.Match(id);
            if (match == null || !match.Success || string.IsNullOrWhiteSpace(title)) continue;

            var author = Child(entry, "content");
            var hasAuthor = !string.IsNullOrWhiteSpace(author);
            candidates.Add(new ResourceCandidate(
                Title: Truncate(title.Trim(), 100),
                Url: $"{Host}/ebooks/{match.Groups[1].Value}",
                Author: hasAuthor ? Truncate(author!.Trim(), 100) : "Проект Гутенберг",
                SourceName: DisplayName,
                Description: hasAuthor ? $"Книга от {author!.Trim()} в Проект Гутенберг." : "Книга в Проект Гутенберг.",
                LicenseName: LicenseName));

            if (candidates.Count == maxResults) break;
        }

        return candidates;
    }

    private static string? Child(XElement entry, string name) =>
        entry.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();
}
