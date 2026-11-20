using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// A school class (e.g. "5А") a teacher assigns exams to. It is only a label: students are not
    /// registered and identify themselves by name and class number when they enter an exam.
    /// </summary>
    public class SchoolClass
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Въведете име на класа")]
        [MaxLength(50, ErrorMessage = "Името не може да бъде по-дълго от 50 символа")]
        [Display(Name = "Клас")]
        public string Name { get; set; } = string.Empty;

        public DateTime CreatedOn { get; set; }

        /// <summary>The teacher who owns the class; null if the account was deleted.</summary>
        public string? OwnerId { get; set; }
        public ApplicationUser? Owner { get; set; }

        public ICollection<ExamAssignment> Exams { get; set; } = new List<ExamAssignment>();
    }
}
