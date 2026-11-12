using SchoolLibrary.Data.Enums;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolLibrary.Data.Models
{
    public class GradeLevel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Номерът е задължителен")]
        [Display(Name = "Номер на клас")]
        [Range(5, 12, ErrorMessage = "Номерът на клас трябва да бъде между 5 и 12")]
        public int Number { get; set; }

        [NotMapped]
        [Display(Name = "Име на клас")]
        public string Name => $"{(Grades)Number} клас";

        public ICollection<Resource> Resources { get; set; } =
        new List<Resource>();
    }
}
