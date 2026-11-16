using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Services.Licenses;

namespace SchoolLibrary.Areas.Admin.Models
{
    /// <summary>The fields of the license form. A view model, so nothing but these can be set by a posted form.</summary>
    public class LicenseFormViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Името на лиценза е задължително")]
        [MaxLength(LicenseService.MaxNameLength, ErrorMessage = "Името не може да бъде по-дълго от 100 символа")]
        [Display(Name = "Име")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(LicenseService.MaxDescriptionLength, ErrorMessage = "Описанието не може да бъде по-дълго от 300 символа")]
        [Display(Name = "Описание")]
        public string? Description { get; set; }

        [MaxLength(LicenseService.MaxUrlLength, ErrorMessage = "Уеб адресът не може да бъде по-дълъг от 500 символа")]
        [Display(Name = "Текст на лиценза (уеб адрес)")]
        public string? Url { get; set; }

        public LicenseInput ToInput() => new(Name, Description, Url);
    }
}
