using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolLibrary.Data.Models;

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

    // Данни за падащите менюта
    public List<Category> Categories { get; set; } = new();
    public List<GradeLevel> GradeLevels { get; set; } = new();
    public List<SelectListItem> Owners { get; set; } = new();

    // Резултата от филтрирането
    public List<Resource> Resources { get; set; } = new();
}