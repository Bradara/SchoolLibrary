using SchoolLibrary.Data.Common;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Services.Discovery;

public class ResourceDiscoveryService : IResourceDiscoveryService
{
    public const int MinTopicLength = 2;
    public const int MaxTopicLength = 100;
    public const int ResultsPerSource = 10;
    public static readonly TimeSpan DefaultSourceTimeout = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyList<IResourceSourceProvider> _providers;
    private readonly IResourceService _resources;
    private readonly TimeSpan _sourceTimeout;

    public ResourceDiscoveryService(IEnumerable<IResourceSourceProvider> providers, IResourceService resources, TimeSpan? sourceTimeout = null)
    {
        _providers = providers.ToList();
        _resources = resources;
        _sourceTimeout = sourceTimeout ?? DefaultSourceTimeout;
    }

    public IReadOnlyList<DiscoverySourceInfo> GetSources() =>
        _providers.Select(p => new DiscoverySourceInfo(p.Key, p.DisplayName)).ToList();

    public async Task<ServiceResult<DiscoveryResult>> SearchAsync(string? topic, IEnumerable<string>? sourceKeys, CancellationToken cancellationToken = default)
    {
        topic = topic?.Trim();
        if (string.IsNullOrEmpty(topic) || topic.Length < MinTopicLength)
        {
            return ServiceResult<DiscoveryResult>.Failure(ServiceError.Invalid, $"Въведете тема от поне {MinTopicLength} символа.");
        }

        if (topic.Length > MaxTopicLength)
        {
            return ServiceResult<DiscoveryResult>.Failure(ServiceError.Invalid, $"Темата не може да бъде по-дълга от {MaxTopicLength} символа.");
        }

        var chosen = sourceKeys?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>();
        var selected = chosen.Count == 0 ? _providers : _providers.Where(p => chosen.Contains(p.Key)).ToList();
        if (selected.Count == 0)
        {
            return ServiceResult<DiscoveryResult>.Failure(ServiceError.Invalid, "Изберете поне един източник.");
        }

        var outcomes = await Task.WhenAll(selected.Select(p => SearchOneAsync(p, topic, _sourceTimeout, cancellationToken)));

        var failed = outcomes.Where(o => o.Candidates == null).Select(o => o.Provider.DisplayName).ToList();
        var candidates = await MarkExistingAsync(outcomes.Where(o => o.Candidates != null).SelectMany(o => o.Candidates!));

        return ServiceResult<DiscoveryResult>.Success(new DiscoveryResult(candidates, failed));
    }

    // One slow or broken source must not break the whole search: it is reported and the rest is shown
    private static async Task<(IResourceSourceProvider Provider, IReadOnlyList<ResourceCandidate>? Candidates)> SearchOneAsync(
        IResourceSourceProvider provider, string topic, TimeSpan sourceTimeout, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(sourceTimeout);

        try
        {
            return (provider, await provider.SearchAsync(topic, ResultsPerSource, timeout.Token));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return (provider, null);
        }
    }

    // Keeps the first candidate for every link, only http(s) links, and tells which ones the catalog already has
    private async Task<IReadOnlyList<ResourceCandidate>> MarkExistingAsync(IEnumerable<ResourceCandidate> found)
    {
        var seen = new HashSet<string>();
        var result = new List<ResourceCandidate>();

        foreach (var candidate in found)
        {
            var resource = new Resource { Url = candidate.Url };
            var key = UrlNormalizer.Normalize(candidate.Url);
            if (resource.SafeUrl == null || key == null || !seen.Add(key)) continue;

            var existing = await _resources.FindByUrlAsync(candidate.Url);
            result.Add(candidate with { IsEmbeddable = resource.IsEmbeddable, ExistingResourceId = existing?.Id });
        }

        return result;
    }
}
