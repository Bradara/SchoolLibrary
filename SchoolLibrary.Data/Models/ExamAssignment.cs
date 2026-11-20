using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// A practice quiz given to a class as an exam. The assignment carries what makes it an exam: the
    /// entry code, the time limit, proctoring, whether it accepts new attempts, and whether students
    /// may see their results (off until the teacher releases them).
    /// </summary>
    public class ExamAssignment
    {
        public int Id { get; set; }

        public int QuizId { get; set; }
        public Quiz Quiz { get; set; } = null!;

        public int SchoolClassId { get; set; }
        public SchoolClass SchoolClass { get; set; } = null!;

        /// <summary>The code students type to enter the exam; unique across exams.</summary>
        [Required]
        [MaxLength(12)]
        public string Code { get; set; } = string.Empty;

        /// <summary>Minutes allowed for an attempt. Null means no time limit.</summary>
        public int? TimeLimitMinutes { get; set; }

        /// <summary>How many times a student may leave the page before the attempt is auto-submitted. Null means no proctoring.</summary>
        public int? MaxLeaves { get; set; }

        /// <summary>Whether students can start the exam now.</summary>
        public bool IsOpen { get; set; }

        /// <summary>Whether students may see their score, grade and answers.</summary>
        public bool ResultsReleased { get; set; }

        public DateTime CreatedOn { get; set; }

        public ICollection<QuizAttempt> Attempts { get; set; } = new List<QuizAttempt>();
    }
}
