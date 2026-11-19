using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>One answer option of a question. A single-choice question has exactly one correct option.</summary>
    public class AnswerOption
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Въведете текст на отговора")]
        [MaxLength(500, ErrorMessage = "Отговорът не може да бъде по-дълъг от 500 символа")]
        [Display(Name = "Отговор")]
        public string Text { get; set; } = string.Empty;

        public bool IsCorrect { get; set; }

        /// <summary>Display order within the question.</summary>
        public int Order { get; set; }

        public int QuestionId { get; set; }
        public Question Question { get; set; } = null!;
    }
}
