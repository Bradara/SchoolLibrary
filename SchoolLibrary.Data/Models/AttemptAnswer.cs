using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>The student's answer to one question within an attempt.</summary>
    public class AttemptAnswer
    {
        public int Id { get; set; }

        public int AttemptId { get; set; }
        public QuizAttempt Attempt { get; set; } = null!;

        public int QuestionId { get; set; }
        public Question Question { get; set; } = null!;

        /// <summary>The option the student picked (single-choice). Null means the question was left unanswered.</summary>
        public int? SelectedOptionId { get; set; }
        public AnswerOption? SelectedOption { get; set; }

        /// <summary>The typed answer (short answer, number, essay or a program); null for choice questions.</summary>
        [MaxLength(10000, ErrorMessage = "Отговорът не може да бъде по-дълъг от 10000 символа")]
        public string? ResponseText { get; set; }

        /// <summary>
        /// For a code question: the result of running the program (status, compiler message and every test's
        /// output) as JSON, shown in the review. Null for the other types and while the answer is not checked yet.
        /// </summary>
        public string? CodeResults { get; set; }

        /// <summary>
        /// For a code question: the program still waits to be run on the sandbox server (exams are checked in the
        /// background, so the student does not wait). Its points count once it has been checked.
        /// </summary>
        public bool CodeCheckPending { get; set; }

        /// <summary>The ticked options, matching pairs or ordering of the answer (questions with several parts).</summary>
        public ICollection<AttemptAnswerChoice> Choices { get; set; } = new List<AttemptAnswerChoice>();

        /// <summary>Whether the answer earned full credit.</summary>
        public bool IsCorrect { get; set; }

        /// <summary>The points the answer earned; fractional with partial credit.</summary>
        public decimal AwardedPoints { get; set; }

        /// <summary>
        /// The points the teacher gave instead of <see cref="AwardedPoints"/>; null means the automatic points count.
        /// Regrading never changes them, so a teacher's decision survives an edit of the question.
        /// </summary>
        public decimal? ManualPoints { get; set; }

        /// <summary>The teacher's note on the answer, shown to the student with the result.</summary>
        [MaxLength(MaxCommentLength, ErrorMessage = "Коментарът не може да бъде по-дълъг от 500 символа")]
        public string? ManualComment { get; set; }

        /// <summary>When the teacher last set the points or the note (UTC).</summary>
        public DateTime? ManualGradedOn { get; set; }

        /// <summary>The points that count for the attempt: the teacher's when given, otherwise the automatic ones.</summary>
        public decimal Points => ManualPoints ?? AwardedPoints;

        public const int MaxCommentLength = 500;

        /// <summary>Set when the teacher still has to grade the answer by hand (essay questions).</summary>
        public bool NeedsReview { get; set; }
    }
}
