using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// One run of a quiz by a student. Students are not registered: they identify themselves by a
    /// self-reported name and class number. The single-attempt rule for exams is enforced by the service.
    /// </summary>
    public class QuizAttempt
    {
        public int Id { get; set; }

        public int QuizId { get; set; }
        public Quiz Quiz { get; set; } = null!;

        [Required(ErrorMessage = "Въведете име")]
        [MaxLength(100, ErrorMessage = "Името не може да бъде по-дълго от 100 символа")]
        [Display(Name = "Име")]
        public string StudentName { get; set; } = string.Empty;

        [MaxLength(20, ErrorMessage = "Номерът не може да бъде по-дълъг от 20 символа")]
        [Display(Name = "Номер в класа")]
        public string? StudentNumber { get; set; }

        public DateTime StartedOn { get; set; }

        /// <summary>When the attempt was graded; null while it is still in progress.</summary>
        public DateTime? SubmittedOn { get; set; }

        /// <summary>Number of correct answers.</summary>
        public int Score { get; set; }

        /// <summary>Number of questions (the maximum score).</summary>
        public int MaxScore { get; set; }

        public ICollection<AttemptAnswer> Answers { get; set; } = new List<AttemptAnswer>();
    }
}
