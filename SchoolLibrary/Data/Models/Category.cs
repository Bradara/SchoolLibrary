using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    public class Category
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Името е задължително")]
        [MaxLength(50, ErrorMessage = "Името не може да бъде по-дълго от 50 символа")]
        [Display(Name = "Име на категория")]
        public string Name { get; set; } = string.Empty;

        public ICollection<Resource> Resources { get; set; } = new List<Resource>();

    }
}
