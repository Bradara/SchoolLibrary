using SchoolLibrary.Data.Common;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Licenses;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Services.Discovery;

public class ResourceDiscoveryService : IResourceDiscoveryService
{
    public const int MinTopicLength = 2;
    public const int MaxTopicLength = 100;
    public const int ResultsPerSource = 10;
    public const int MaxAddAtOnce = 30;
    public static readonly TimeSpan DefaultSourceTimeout = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyList<IResourceSourceProvider> _providers;
    private readonly IResourceService _resources;
    private readonly ILicenseService _licenses;
    private readonly TimeSpan _sourceTimeout;

    public ResourceDiscoveryService(IEnumerable<IResourceSourceProvider> providers, IResourceService resources, ILicenseService licenses, TimeSpan? sourceTimeout = null)
    {
        _providers = providers.ToList();
        _resources = resources;
        _licenses = licenses;
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

        // Words like "и", "на", "или" are dropped; the sources get only the words that name the topic
        var terms = SearchTerms.Extract(topic);
        if (terms.Count == 0)
        {
            return ServiceResult<DiscoveryResult>.Failure(ServiceError.Invalid, "Въведете конкретна тема. Думи като „и“, „на“, „или“ се пропускат.");
        }

        var query = string.Join(' ', terms);

        var chosen = sourceKeys?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>();
        var selected = chosen.Count == 0 ? _providers : _providers.Where(p => chosen.Contains(p.Key)).ToList();
        if (selected.Count == 0)
        {
            return ServiceResult<DiscoveryResult>.Failure(ServiceError.Invalid, "Изберете поне един източник.");
        }

        var outcomes = await Task.WhenAll(selected.Select(p => SearchOneAsync(p, query, _sourceTimeout, cancellationToken)));

        var failed = outcomes.Where(o => o.Candidates == null).Select(o => o.Provider.DisplayName).ToList();
        var candidates = await MarkExistingAsync(outcomes.Where(o => o.Candidates != null).SelectMany(o => o.Candidates!));

        return ServiceResult<DiscoveryResult>.Success(new DiscoveryResult(candidates, failed, terms));
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

    public async Task<ServiceResult<DiscoveryAddResult>> AddAsync(
        IReadOnlyList<CandidateSelection>? selected,
        IReadOnlyList<int>? categoryIds,
        IReadOnlyList<int>? gradeLevelIds,
        string ownerId)
    {
        if (selected == null || selected.Count == 0)
        {
            return ServiceResult<DiscoveryAddResult>.Failure(ServiceError.Invalid, "Изберете поне един ресурс.");
        }

        if (selected.Count > MaxAddAtOnce)
        {
            return ServiceResult<DiscoveryAddResult>.Failure(ServiceError.Invalid, $"Можете да добавите най-много {MaxAddAtOnce} ресурса наведнъж.");
        }

        if (categoryIds == null || categoryIds.Count == 0)
        {
            return ServiceResult<DiscoveryAddResult>.Failure(ServiceError.Invalid, "Изберете поне един предмет.");
        }

        if (gradeLevelIds == null || gradeLevelIds.Count == 0)
        {
            return ServiceResult<DiscoveryAddResult>.Failure(ServiceError.Invalid, "Изберете поне един клас.");
        }

        var licenses = (await _licenses.GetOptionsAsync())
            .ToDictionary(l => l.Name, l => l.Id, StringComparer.OrdinalIgnoreCase);

        var added = new List<string>();
        var skipped = new List<string>();

        foreach (var item in selected)
        {
            var title = item.Title?.Trim() ?? string.Empty;
            if (item.LicenseName == null || !licenses.TryGetValue(item.LicenseName.Trim(), out var licenseId))
            {
                skipped.Add($"{title}: лицензът не е намерен в платформата.");
                continue;
            }

            var result = await _resources.CreateAsync(new ResourceInput
            {
                Title = title,
                Author = item.Author ?? string.Empty,
                Source = item.SourceName,
                Description = item.Description,
                Url = item.Url ?? string.Empty,
                LicenseId = licenseId,
                Kind = ResourceKind.External,
                CategoryIds = categoryIds.ToList(),
                GradeLevelIds = gradeLevelIds.ToList()
            }, ownerId);

            if (result.Succeeded) added.Add(result.Value!.Title);
            else skipped.Add($"{title}: {result.Message}");
        }

        return ServiceResult<DiscoveryAddResult>.Success(new DiscoveryAddResult(added, skipped));
    }
}
