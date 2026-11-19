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

        /// <summary>Unguessable id used in the exam URLs, so one student cannot open another's attempt.</summary>
        public Guid Token { get; set; }

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

        /// <summary>Points scored.</summary>
        public int Score { get; set; }

        /// <summary>The maximum score (the sum of the served questions' points).</summary>
        public int MaxScore { get; set; }

        /// <summary>How many times the student left the exam tab/window (proctoring).</summary>
        public int LeaveCount { get; set; }

        /// <summary>Set when the student left more times than the exam allows.</summary>
        public bool Flagged { get; set; }

        /// <summary>The questions served to this attempt (the snapshot taken when it started).</summary>
        public ICollection<AttemptQuestion> Questions { get; set; } = new List<AttemptQuestion>();

        public ICollection<AttemptAnswer> Answers { get; set; } = new List<AttemptAnswer>();
    }
}
