using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>The terms under which a resource may be used and shared.</summary>
    public class License
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Името на лиценза е задължително")]
        [MaxLength(100, ErrorMessage = "Името не може да бъде по-дълго от 100 символа")]
        [Display(Name = "Лиценз")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(300, ErrorMessage = "Описанието не може да бъде по-дълго от 300 символа")]
        [Display(Name = "Описание")]
        public string? Description { get; set; }

        /// <summary>Link to the full license text, when there is one.</summary>
        [MaxLength(500, ErrorMessage = "Уеб адресът не може да бъде по-дълъг от 500 символа")]
        [Display(Name = "Текст на лиценза")]
        public string? Url { get; set; }
    }
}
