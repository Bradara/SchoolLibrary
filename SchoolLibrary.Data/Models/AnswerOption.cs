using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolLibrary.Data.Models
{
    /// <summary>One answer option of a question. A single-choice question has exactly one correct option.</summary>
    public class AnswerOption
    {
        /// <summary>The fraction of a fully correct option.</summary>
        public const decimal FullCredit = 100m;

        public int Id { get; set; }

        [Required(ErrorMessage = "Въведете текст на отговора")]
        [MaxLength(500, ErrorMessage = "Отговорът не може да бъде по-дълъг от 500 символа")]
        [Display(Name = "Отговор")]
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// The share of the question's points this option is worth, as a percentage from -100 to 100
        /// (the Moodle <c>fraction</c>). A correct single-choice option is 100, a wrong one 0.
        /// </summary>
        [Range(-100, 100, ErrorMessage = "Делът трябва да е между -100 и 100")]
        public decimal Fraction { get; set; }

        /// <summary>Whether the option earns full credit. Setting it stores 100 or 0 in <see cref="Fraction"/>.</summary>
        [NotMapped]
        public bool IsCorrect
        {
            get => Fraction >= FullCredit;
            set => Fraction = value ? FullCredit : 0m;
        }

        /// <summary>
        /// For a matching question: the answer that goes with this option's <see cref="Text"/> (the right side
        /// of the pair). Null for the other question types.
        /// </summary>
        [MaxLength(500, ErrorMessage = "Отговорът не може да бъде по-дълъг от 500 символа")]
        public string? MatchText { get; set; }

        /// <summary>
        /// For a numerical question: how far a typed number may be from this option's value and still match
        /// (0 means exactly). Null for the other question types.
        /// </summary>
        public decimal? Tolerance { get; set; }

        /// <summary>Display order within the question.</summary>
        public int Order { get; set; }

        public int QuestionId { get; set; }
        public Question Question { get; set; } = null!;
    }
}
