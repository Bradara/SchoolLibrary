using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.ViewModels;

// Shared by Create and Edit: resource fields plus the checkbox selections
public class ResourceFormViewModel
{
    public int Id { get; set; }

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

    [Required(ErrorMessage = "Уеб адресът е задължителен")]
    [MaxLength(500, ErrorMessage = "Уеб адресът не може да бъде по-дълъг от 500 символа")]
    [Display(Name = "Уеб адрес")]
    public string Url { get; set; } = string.Empty;

    [Display(Name = "Вид")]
    public ResourceKind Kind { get; set; } = ResourceKind.Lesson;

    [Required(ErrorMessage = "Изберете лиценз")]
    [Display(Name = "Лиценз")]
    public int? LicenseId { get; set; }

    // Ids of the ticked checkboxes
    [Display(Name = "Предмети")]
    public List<int> SelectedCategoryIds { get; set; } = new();

    [Display(Name = "Класове")]
    public List<int> SelectedGradeLevelIds { get; set; } = new();

    // Set by the controller when the link is already in the library, to offer a link to that resource
    public int? DuplicateOfId { get; set; }

    // All available options, filled by the controller (not posted back)
    public List<Category> AllCategories { get; set; } = new();
    public List<GradeLevel> AllGradeLevels { get; set; } = new();
    public List<License> AllLicenses { get; set; } = new();
}
