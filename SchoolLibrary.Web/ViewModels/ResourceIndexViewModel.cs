using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.ViewModels;

public class ResourceIndexViewModel
{
    /// <summary>How many of the newest resources are shown under the subject tiles.</summary>
    public const int NewestOnTiles = 6;

    // Филтри (входни полета от формата)
    // null = all resources, otherwise one section of the library
    public ResourceKind? Kind { get; set; }
    public string? SearchTitle { get; set; }
    public int? CategoryId { get; set; }
    public int? GradeLevelId { get; set; }
    public string? OwnerId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public ResourceSortOrder SortOrder { get; set; } = ResourceSortOrder.DateDesc;

    /// <summary>"Виж всички": the results instead of the subject tiles.</summary>
    public bool All { get; set; }

    /// <summary>The same choices as a query, for the links of the page.</summary>
    public CatalogQuery Query => new()
    {
        Kind = Kind,
        SearchTitle = SearchTitle,
        CategoryId = CategoryId,
        GradeLevelId = GradeLevelId,
        OwnerId = OwnerId,
        FromDate = FromDate,
        ToDate = ToDate,
        SortOrder = SortOrder,
        All = All
    };

    /// <summary>The numbers on the kind switch and the subject tiles.</summary>
    public CatalogFacets Facets { get; set; } =
        new(new Dictionary<ResourceKind, int>(), Array.Empty<SubjectFacet>());

    // Данни за падащите менюта
    public IReadOnlyList<Category> Categories { get; set; } = new List<Category>();
    public IReadOnlyList<GradeLevel> GradeLevels { get; set; } = new List<GradeLevel>();
    public List<SelectListItem> Owners { get; set; } = new();

    // Резултата от филтрирането (текущата страница)
    public IReadOnlyList<Resource> Resources { get; set; } = new List<Resource>();

    // Ресурсите от текущата страница, които потребителят е добавил в любими
    public IReadOnlySet<int> FavoriteIds { get; set; } = new HashSet<int>();

    // Странициране
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}
