using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Data.Models
{
    /// <summary>A quiz question of one of the <see cref="QuestionType"/> kinds, with its answer options.</summary>
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

        /// <summary>Whether a short answer must match the letter case of an accepted answer.</summary>
        public bool CaseSensitive { get; set; }

        /// <summary>For a code question: the language id the program is written in ("python3", "csharp").</summary>
        [MaxLength(20)]
        public string? CodeLanguage { get; set; }

        /// <summary>For a code question: the code the answer box starts with (e.g. an empty Main method).</summary>
        [MaxLength(10000, ErrorMessage = "Началният код не може да бъде по-дълъг от 10000 символа")]
        [Display(Name = "Начален код")]
        public string? AnswerPreload { get; set; }

        /// <summary>Display order within the quiz.</summary>
        public int Order { get; set; }

        /// <summary>The quiz this question is attached to directly; null for a bank question in a section.</summary>
        public int? QuizId { get; set; }
        public Quiz? Quiz { get; set; }

        /// <summary>The section (bank topic) this question belongs to; null until it is filed in one.</summary>
        public int? SectionId { get; set; }
        public Section? Section { get; set; }

        public ICollection<AnswerOption> Options { get; set; } = new List<AnswerOption>();

        /// <summary>For a code question: the tests the program is run with. Empty for the other types.</summary>
        public ICollection<CodeTestCase> TestCases { get; set; } = new List<CodeTestCase>();
    }
}
