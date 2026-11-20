using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// A quiz created by a teacher. A practice quiz is open and optionally tied to a lesson; an exam
    /// is entered anonymously by its <see cref="Code"/>, is timed and allows a single attempt.
    /// </summary>
    public class Quiz
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Въведете заглавие")]
        [MaxLength(200, ErrorMessage = "Заглавието не може да бъде по-дълго от 200 символа")]
        [Display(Name = "Заглавие")]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000, ErrorMessage = "Описанието не може да бъде по-дълго от 1000 символа")]
        [Display(Name = "Описание")]
        public string? Description { get; set; }

        public QuizMode Mode { get; set; }

        /// <summary>The code students type to enter an exam. Null for practice quizzes; unique when set.</summary>
        [MaxLength(12)]
        public string? Code { get; set; }

        /// <summary>Minutes allowed for an exam attempt. Null means no time limit (practice).</summary>
        public int? TimeLimitMinutes { get; set; }

        /// <summary>
        /// Exam proctoring: how many times a student may leave the tab/window before the attempt is
        /// flagged and auto-submitted. Null means no proctoring.
        /// </summary>
        public int? MaxLeaves { get; set; }

        /// <summary>Whether the questions are served in random order (fixed per exam attempt).</summary>
        [Display(Name = "Разбъркай въпросите")]
        public bool ShuffleQuestions { get; set; } = true;

        /// <summary>Whether each question's answer options are served in random order.</summary>
        [Display(Name = "Разбъркай отговорите")]
        public bool ShuffleOptions { get; set; } = true;

        /// <summary>Whether students can currently see and take the quiz.</summary>
        public bool IsPublished { get; set; }

        public DateTime CreatedOn { get; set; }

        /// <summary>Optional lesson this quiz belongs to; the link becomes null if the lesson is deleted.</summary>
        public int? ResourceId { get; set; }
        public Resource? Resource { get; set; }

        /// <summary>The teacher who owns the quiz; null if the account was deleted.</summary>
        public string? OwnerId { get; set; }
        public ApplicationUser? Owner { get; set; }

        public ICollection<Question> Questions { get; set; } = new List<Question>();

        /// <summary>The quiz's composition: pinned questions and random-from-section draws, ordered.</summary>
        public ICollection<QuizItem> Items { get; set; } = new List<QuizItem>();
    }
}
