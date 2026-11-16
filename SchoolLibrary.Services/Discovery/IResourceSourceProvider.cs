namespace SchoolLibrary.Services.Discovery;

/// <summary>
/// One place resources can be searched for. The list of providers is fixed in code: users never give
/// an address to fetch, so the server cannot be made to call arbitrary hosts.
/// </summary>
public interface IResourceSourceProvider
{
    /// <summary>Stable identifier used in forms and the query string.</summary>
    string Key { get; }

    /// <summary>Name shown to users (Bulgarian).</summary>
    string DisplayName { get; }

    /// <summary>Searches the source. May throw when the source does not answer; the caller handles that.</summary>
    Task<IReadOnlyList<ResourceCandidate>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}
