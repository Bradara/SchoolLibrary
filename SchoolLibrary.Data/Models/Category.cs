using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Data.Common;

namespace SchoolLibrary.Data.Models
{
    public class Category
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Името е задължително")]
        [MaxLength(50, ErrorMessage = "Името не може да бъде по-дълго от 50 символа")]
        [Display(Name = "Име на предмет")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Bootstrap Icons class on the catalog tiles and cards (one of <see cref="SubjectAppearance.Icons"/>).</summary>
        [MaxLength(SubjectAppearance.IconMaxLength)]
        [Display(Name = "Иконка")]
        public string Icon { get; set; } = SubjectAppearance.DefaultIcon;

        /// <summary>Color of the tile and of the card stripe, #rrggbb (one of <see cref="SubjectAppearance.Colors"/>).</summary>
        [MaxLength(SubjectAppearance.ColorMaxLength)]
        [Display(Name = "Цвят")]
        public string Color { get; set; } = SubjectAppearance.DefaultColor;

        public ICollection<Resource> Resources { get; set; } = new List<Resource>();

    }
}
