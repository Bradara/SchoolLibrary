using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

        /// <summary>The class exam this attempt belongs to; null for an exam-mode quiz entered by its own code.</summary>
        public int? ExamAssignmentId { get; set; }
        public ExamAssignment? ExamAssignment { get; set; }

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

        /// <summary>Points scored; fractional when a question earns partial credit.</summary>
        public decimal Score { get; set; }

        /// <summary>The maximum score (the sum of the served questions' points).</summary>
        public decimal MaxScore { get; set; }

        /// <summary>How many times the student left the exam tab/window (proctoring).</summary>
        public int LeaveCount { get; set; }

        /// <summary>Set when the student left more times than the exam allows.</summary>
        public bool Flagged { get; set; }

        /// <summary>
        /// How many seconds after the time limit the attempt was submitted (checked on the server); null when it
        /// was on time or the exam has no time limit.
        /// </summary>
        public int? LateBySeconds { get; set; }

        /// <summary>The questions served to this attempt (the snapshot taken when it started).</summary>
        public ICollection<AttemptQuestion> Questions { get; set; } = new List<AttemptQuestion>();

        public ICollection<AttemptAnswer> Answers { get; set; } = new List<AttemptAnswer>();

        /// <summary>The checks of code answers made before submitting (each press of "Провери").</summary>
        public ICollection<CodeCheck> CodeChecks { get; set; } = new List<CodeCheck>();

        /// <summary>The time limit in force: the class exam's when there is one, otherwise the quiz's.</summary>
        [NotMapped]
        public int? TimeLimitMinutes => ExamAssignment != null ? ExamAssignment.TimeLimitMinutes : Quiz?.TimeLimitMinutes;

        /// <summary>The proctoring allowance in force: the class exam's when there is one, otherwise the quiz's.</summary>
        [NotMapped]
        public int? MaxLeaves => ExamAssignment != null ? ExamAssignment.MaxLeaves : Quiz?.MaxLeaves;

        /// <summary>Whether code questions offer completion hints: the class exam's setting, always for a quiz exam.</summary>
        [NotMapped]
        public bool CodeHints => ExamAssignment?.CodeHints ?? true;

        /// <summary>Whether the student may see the result: always for a quiz exam, once released for a class exam.</summary>
        [NotMapped]
        public bool ResultVisible => ExamAssignment == null || ExamAssignment.ResultsReleased;
    }
}
