namespace SchoolLibrary.Services.Discovery;

/// <summary>A resource a source suggested. Nothing is saved until a teacher approves it.</summary>
/// <param name="LicenseName">Name of a license from the seeded list (for example "CC BY-SA 4.0").</param>
/// <param name="IsEmbeddable">True when the page can be shown inside the platform.</param>
/// <param name="ExistingResourceId">Id of the resource that already uses this link, or null when it is new.</param>
public record ResourceCandidate(
    string Title,
    string Url,
    string Author,
    string SourceName,
    string Description,
    string LicenseName,
    bool IsEmbeddable = false,
    int? ExistingResourceId = null);

/// <summary>A source the search can use, as shown in the list of checkboxes.</summary>
public record DiscoverySourceInfo(string Key, string DisplayName);

/// <summary>Candidates from all chosen sources, the sources that did not answer and the words that were actually searched for.</summary>
public record DiscoveryResult(IReadOnlyList<ResourceCandidate> Candidates, IReadOnlyList<string> FailedSources, IReadOnlyList<string>? Terms = null);

/// <summary>A suggestion a teacher ticked to add to the library.</summary>
public record CandidateSelection(
    string Title,
    string Url,
    string Author,
    string? SourceName,
    string? Description,
    string LicenseName);

/// <summary>What happened to the ticked suggestions: titles that were added and short reasons for those that were not.</summary>
public record DiscoveryAddResult(IReadOnlyList<string> Added, IReadOnlyList<string> Skipped);
