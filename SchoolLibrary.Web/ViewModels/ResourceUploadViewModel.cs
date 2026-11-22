using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.ViewModels;

// The form for uploading an author's own file (PDF, EPUB or Markdown). It has a file instead of a URL.
public class ResourceUploadViewModel
{
    [Display(Name = "Файл")]
    public IFormFile? File { get; set; }

    [Required(ErrorMessage = "Заглавието е задължително")]
    [MaxLength(100, ErrorMessage = "Заглавието не може да бъде по-дълго от 100 символа")]
    [Display(Name = "Заглавие")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Посочете автора на материала.")]
    [MaxLength(100, ErrorMessage = "Авторът не може да бъде по-дълъг от 100 символа")]
    [Display(Name = "Автор")]
    public string Author { get; set; } = string.Empty;

    [MaxLength(100, ErrorMessage = "Източникът не може да бъде по-дълъг от 100 символа")]
    [Display(Name = "Източник")]
    public string? Source { get; set; }

    [MaxLength(1000, ErrorMessage = "Описанието не може да бъде по-дълго от 1000 символа")]
    [Display(Name = "Описание")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Изберете лиценз")]
    [Display(Name = "Лиценз")]
    public int? LicenseId { get; set; }

    // The ticked audience groups; see ResourceFormViewModel.Audience for the hidden "None"
    [Display(Name = "Видимост")]
    public List<ResourceAudience> Audience { get; set; } = new()
    {
        ResourceAudience.Guests, ResourceAudience.Students, ResourceAudience.Parents, ResourceAudience.Teachers
    };

    [Display(Name = "Предмети")]
    public List<int> SelectedCategoryIds { get; set; } = new();

    [Display(Name = "Класове")]
    public List<int> SelectedGradeLevelIds { get; set; } = new();

    // All available options, filled by the controller (not posted back)
    public List<Category> AllCategories { get; set; } = new();
    public List<GradeLevel> AllGradeLevels { get; set; } = new();
    public List<License> AllLicenses { get; set; } = new();
}
