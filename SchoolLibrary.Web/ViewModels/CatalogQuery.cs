using System.Globalization;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.ViewModels;

/// <summary>
/// The state of the catalog as it is in the address: the chosen kind, filters and order. The links of the
/// catalog (kind switch, grade chips, subject tiles, the × of a filter, the pages) are this state with one
/// value changed, written with <c>with</c>, e.g. <c>Query with { CategoryId = null }</c>.
/// </summary>
public record CatalogQuery
{
    public ResourceKind? Kind { get; init; }
    public string? SearchTitle { get; init; }
    public int? CategoryId { get; init; }
    public int? GradeLevelId { get; init; }
    public string? OwnerId { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public ResourceSortOrder SortOrder { get; init; } = ResourceSortOrder.DateDesc;

    /// <summary>"Виж всички": the results instead of the subject tiles, without any other filter.</summary>
    public bool All { get; init; }

    /// <summary>
    /// Whether the page shows the subject tiles: nothing narrows the catalog to a list yet. The kind and the
    /// grade only change the numbers on the tiles.
    /// </summary>
    public bool ShowsTiles =>
        !All
        && CategoryId == null
        && string.IsNullOrWhiteSpace(SearchTitle)
        && string.IsNullOrEmpty(OwnerId)
        && FromDate == null
        && ToDate == null;

    /// <summary>Whether a filter other than the kind is chosen (the "Изчисти всичко" link is shown then).</summary>
    public bool HasFilters =>
        CategoryId != null
        || GradeLevelId != null
        || !string.IsNullOrWhiteSpace(SearchTitle)
        || !string.IsNullOrEmpty(OwnerId)
        || FromDate != null
        || ToDate != null;

    /// <summary>The same section with no filters; the order stays.</summary>
    public CatalogQuery Cleared() => new() { Kind = Kind, SortOrder = SortOrder };

    /// <summary>
    /// The query string values for a link (asp-all-route-data). Empty values and the default order are left
    /// out, so the addresses stay short; page 1 is left out as well.
    /// </summary>
    public Dictionary<string, string?> ToRouteValues(int? page = null)
    {
        var values = new Dictionary<string, string?>();

        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[name] = value;
            }
        }

        Add("kind", Kind?.ToString());
        Add("searchTitle", SearchTitle?.Trim());
        Add("categoryId", CategoryId?.ToString(CultureInfo.InvariantCulture));
        Add("gradeLevelId", GradeLevelId?.ToString(CultureInfo.InvariantCulture));
        Add("ownerId", OwnerId);
        Add("fromDate", FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("toDate", ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        if (SortOrder != ResourceSortOrder.DateDesc)
        {
            Add("sortOrder", SortOrder.ToString());
        }

        if (All)
        {
            Add("all", "true");
        }

        if (page is > 1)
        {
            Add("page", page.Value.ToString(CultureInfo.InvariantCulture));
        }

        return values;
    }

    /// <summary>
    /// The chosen filters as removable pills, each with the query without it. The kind is not one of them:
    /// the kind switch shows it. Names come from the lists of the page; an unknown id is still removable.
    /// </summary>
    public IReadOnlyList<ActiveFilter> ActiveFilters(
        IEnumerable<(int Id, string Name)> subjects,
        IEnumerable<(int Id, string Name)> grades,
        IEnumerable<(string Id, string Name)> owners)
    {
        var filters = new List<ActiveFilter>();

        if (CategoryId is { } categoryId)
        {
            var name = subjects.FirstOrDefault(s => s.Id == categoryId).Name ?? "?";
            filters.Add(new ActiveFilter("Предмет: " + name, this with { CategoryId = null }));
        }

        if (GradeLevelId is { } gradeLevelId)
        {
            var name = grades.FirstOrDefault(g => g.Id == gradeLevelId).Name ?? "?";
            filters.Add(new ActiveFilter("Клас: " + name, this with { GradeLevelId = null }));
        }

        if (!string.IsNullOrWhiteSpace(SearchTitle))
        {
            filters.Add(new ActiveFilter("„" + SearchTitle.Trim() + "“", this with { SearchTitle = null }));
        }

        if (!string.IsNullOrEmpty(OwnerId))
        {
            var name = owners.FirstOrDefault(o => o.Id == OwnerId).Name ?? "?";
            filters.Add(new ActiveFilter("Публикувал: " + name, this with { OwnerId = null }));
        }

        if (FromDate is { } from)
        {
            filters.Add(new ActiveFilter("От " + from.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), this with { FromDate = null }));
        }

        if (ToDate is { } to)
        {
            filters.Add(new ActiveFilter("До " + to.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), this with { ToDate = null }));
        }

        return filters;
    }

    /// <summary>"1 ресурс", "5 ресурса" (Bulgarian has one plural form after a number).</summary>
    public static string ResourceCount(int count) =>
        count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " ресурс" : " ресурса");
}

/// <summary>A chosen filter on the page: its label and the address of the same page without it.</summary>
public record ActiveFilter(string Label, CatalogQuery Without);

/// <summary>A subject tile: the subject with its number, and the query of the page it is on.</summary>
public record SubjectTileModel(SubjectFacet Subject, CatalogQuery Query)
{
    /// <summary>The results of this subject; the kind, the grade and the order stay.</summary>
    public CatalogQuery Link => Query with { CategoryId = Subject.CategoryId, All = false };
}
