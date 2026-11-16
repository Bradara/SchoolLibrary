using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Discovery;

/// <summary>Finds resource suggestions on a fixed list of trusted sources.</summary>
public interface IResourceDiscoveryService
{
    /// <summary>The sources a user can choose from.</summary>
    IReadOnlyList<DiscoverySourceInfo> GetSources();

    /// <summary>
    /// Searches the chosen sources (all when none is chosen) for a topic. Unknown source keys are ignored,
    /// a source that does not answer is reported but does not fail the search. Candidates already in the
    /// catalog are marked with the id of the existing resource.
    /// </summary>
    Task<ServiceResult<DiscoveryResult>> SearchAsync(string? topic, IEnumerable<string>? sourceKeys, CancellationToken cancellationToken = default);
}
