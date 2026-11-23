using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Services.Resources;

/// <summary>Order of the resource list. The names are used as values in the query string.</summary>
public enum ResourceSortOrder
{
    DateDesc,
    DateAsc,
    TitleAsc,
    TitleDesc,
    AuthorAsc,
    AuthorDesc,
    OwnerAsc
}

/// <summary>Search, filter, sort and paging options for the resource list. Every filter is optional.</summary>
public class ResourceFilter
{
    /// <summary>
    /// 12 fills every row of the cards: the catalog shows 4, 3, 2 or 1 of them in a row, depending on the width.
    /// </summary>
    public const int DefaultPageSize = 12;
    public const int MaxPageSize = 100;

    /// <summary>Text looked for in the title and the author, ignoring case.</summary>
    public string? SearchText { get; set; }

    /// <summary>Only lessons or only free resources; null shows both.</summary>
    public ResourceKind? Kind { get; set; }

    public int? CategoryId { get; set; }

    public int? GradeLevelId { get; set; }

    public string? OwnerId { get; set; }

    /// <summary>Only resources added on or after this day.</summary>
    public DateTime? FromDate { get; set; }

    /// <summary>Only resources added on or before this day.</summary>
    public DateTime? ToDate { get; set; }

    public ResourceSortOrder SortOrder { get; set; } = ResourceSortOrder.DateDesc;

    /// <summary>Requested page, starting at 1.</summary>
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>Who is looking: only the resources this viewer may see are listed.</summary>
    public ResourceViewer Viewer { get; set; } = ResourceViewer.Anonymous;

    /// <summary>A copy of the filter, for counting with one of the filters left out.</summary>
    public ResourceFilter Copy() => (ResourceFilter)MemberwiseClone();
}

/// <summary>A subject on the catalog tiles: its look and how many resources the viewer would find in it.</summary>
public record SubjectFacet(int CategoryId, string Name, string Icon, string Color, int Count);

/// <summary>
/// The numbers on the catalog: how many resources each kind would show (the kind switch) and each subject
/// would show (the tiles). Every number keeps the other filters and counts only what the viewer may see.
/// </summary>
public record CatalogFacets(IReadOnlyDictionary<ResourceKind, int> ByKind, IReadOnlyList<SubjectFacet> Subjects)
{
    /// <summary>All resources of every kind, for "Всички" on the kind switch.</summary>
    public int Total => ByKind.Values.Sum();

    public int CountOf(ResourceKind kind) => ByKind.TryGetValue(kind, out var count) ? count : 0;
}

/// <summary>The data a user enters when adding or editing a resource.</summary>
public class ResourceInput
{
    public string Title { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string? Source { get; set; }

    public string? Description { get; set; }

    public string Url { get; set; } = string.Empty;

    public int? LicenseId { get; set; }

    public ResourceKind Kind { get; set; } = ResourceKind.Lesson;

    /// <summary>Who besides the owner and the administrators may see the resource.</summary>
    public ResourceAudience Audience { get; set; } = ResourceAudience.Everyone;

    public List<int> CategoryIds { get; set; } = new();

    public List<int> GradeLevelIds { get; set; } = new();
}

/// <summary>A user who published at least one resource, for the "published by" filter.</summary>
public record ResourceOwnerOption(string Id, string FullName);
