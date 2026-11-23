using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Data.Common;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Categories;

namespace SchoolLibrary.Areas.Admin.Models
{
    /// <summary>The fields of the subject form. A view model, so nothing but these can be set by a posted form.</summary>
    public class CategoryFormViewModel : IValidatableObject
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Името е задължително")]
        [MaxLength(CategoryService.MaxNameLength, ErrorMessage = "Името не може да бъде по-дълго от 50 символа")]
        [Display(Name = "Име на предмет")]
        public string Name { get; set; } = string.Empty;

        // Empty on a new subject: the service then picks the look by the name
        [Display(Name = "Иконка")]
        public string? Icon { get; set; }

        [Display(Name = "Цвят")]
        public string? Color { get; set; }

        // The service checks the same; here the message is shown next to the field
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!string.IsNullOrWhiteSpace(Icon) && !SubjectAppearance.IsValidIcon(Icon))
            {
                yield return new ValidationResult("Изберете иконка от списъка.", new[] { nameof(Icon) });
            }

            if (!string.IsNullOrWhiteSpace(Color) && !SubjectAppearance.IsValidColor(Color))
            {
                yield return new ValidationResult("Изберете цвят от списъка.", new[] { nameof(Color) });
            }
        }

        public CategoryInput ToInput() => new(Name, Icon, Color);

        public static CategoryFormViewModel From(Category category) => new()
        {
            Id = category.Id,
            Name = category.Name,
            Icon = category.Icon,
            Color = category.Color
        };
    }
}
