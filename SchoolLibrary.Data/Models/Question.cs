using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Data.Models
{
    /// <summary>A question in a quiz. The first version supports only single-choice questions.</summary>
    public class Question
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Въведете текст на въпроса")]
        [MaxLength(1000, ErrorMessage = "Въпросът не може да бъде по-дълъг от 1000 символа")]
        [Display(Name = "Въпрос")]
        public string Text { get; set; } = string.Empty;

        public QuestionType Type { get; set; }

        /// <summary>How many points the question is worth. Defaults to 1.</summary>
        public int Points { get; set; } = 1;

        /// <summary>Display order within the quiz.</summary>
        public int Order { get; set; }

        public int QuizId { get; set; }
        public Quiz Quiz { get; set; } = null!;

        public ICollection<AnswerOption> Options { get; set; } = new List<AnswerOption>();
    }
}
