using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// A teacher's own thresholds for the 2–6 grades: the lowest percentage of the points for a 3, 4, 5 and 6.
    /// Exams point at the scale, so changing it changes the grades of every exam that uses it.
    /// </summary>
    public class GradeScale
    {
        public const int NameMaxLength = 100;

        public int Id { get; set; }

        [Required(ErrorMessage = "Въведете име на скалата")]
        [MaxLength(NameMaxLength, ErrorMessage = "Името не може да бъде по-дълго от 100 символа")]
        [Display(Name = "Име")]
        public string Name { get; set; } = string.Empty;

        /// <summary>The lowest percentage (0–100) for a 3; below it the grade is a 2.</summary>
        public decimal Three { get; set; }

        public decimal Four { get; set; }

        public decimal Five { get; set; }

        public decimal Six { get; set; }

        public DateTime CreatedOn { get; set; }

        /// <summary>The teacher who owns the scale; null if the account was deleted.</summary>
        public string? OwnerId { get; set; }
        public ApplicationUser? Owner { get; set; }

        public ICollection<ExamAssignment> Exams { get; set; } = new List<ExamAssignment>();
    }
}
