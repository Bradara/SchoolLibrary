using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Services.OnlineLibraries;

namespace SchoolLibrary.Areas.Admin.Models
{
    /// <summary>The fields of the online library form. A view model, so nothing but these can be set by a posted form.</summary>
    public class OnlineLibraryFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Името на библиотеката е задължително")]
        [MaxLength(OnlineLibraryService.MaxNameLength, ErrorMessage = "Името не може да бъде по-дълго от 100 символа")]
        [Display(Name = "Име")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(OnlineLibraryService.MaxDescriptionLength, ErrorMessage = "Описанието не може да бъде по-дълго от 300 символа")]
        [Display(Name = "Кратко описание")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Уеб адресът е задължителен")]
        [MaxLength(OnlineLibraryService.MaxUrlLength, ErrorMessage = "Уеб адресът не може да бъде по-дълъг от 500 символа")]
        [Display(Name = "Уеб адрес")]
        public string Url { get; set; } = string.Empty;

        [Display(Name = "Иконка")]
        public string? Icon { get; set; }

        [Range(0, 10000, ErrorMessage = "Редът трябва да е число между 0 и 10000")]
        [Display(Name = "Ред (по-малкото е по-напред)")]
        public int SortOrder { get; set; } = 100;

        public OnlineLibraryInput ToInput() => new(Name, Description, Url, Icon, SortOrder);
    }
}
