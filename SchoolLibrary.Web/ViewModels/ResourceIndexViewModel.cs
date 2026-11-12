using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.ViewModels;

public class ResourceIndexViewModel
{
    // Филтри (входни полета от формата)
    public string? SearchTitle { get; set; }
    public int? CategoryId { get; set; }
    public int? GradeLevelId { get; set; }
    public string? OwnerId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public ResourceSortOrder SortOrder { get; set; } = ResourceSortOrder.DateDesc;

    // Данни за падащите менюта
    public IReadOnlyList<Category> Categories { get; set; } = new List<Category>();
    public IReadOnlyList<GradeLevel> GradeLevels { get; set; } = new List<GradeLevel>();
    public List<SelectListItem> Owners { get; set; } = new();

    // Резултата от филтрирането (текущата страница)
    public IReadOnlyList<Resource> Resources { get; set; } = new List<Resource>();

    // Странициране
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
}
