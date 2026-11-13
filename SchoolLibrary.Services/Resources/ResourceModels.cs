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
    public const int DefaultPageSize = 10;
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

    public List<int> CategoryIds { get; set; } = new();

    public List<int> GradeLevelIds { get; set; } = new();
}

/// <summary>A user who published at least one resource, for the "published by" filter.</summary>
public record ResourceOwnerOption(string Id, string FullName);
