using System.ComponentModel.DataAnnotations;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// One test of a code question: the program is run with <see cref="Stdin"/> as its input and its output
    /// must equal <see cref="Expected"/> (as the CodeRunner test cases in Moodle).
    /// </summary>
    public class CodeTestCase
    {
        public const int MaxTextLength = 10000;

        public int Id { get; set; }

        /// <summary>The text given to the program on standard input; may be empty.</summary>
        [MaxLength(MaxTextLength, ErrorMessage = "Входът не може да бъде по-дълъг от 10000 символа")]
        [Display(Name = "Вход")]
        public string Stdin { get; set; } = string.Empty;

        /// <summary>The output the program must print.</summary>
        [MaxLength(MaxTextLength, ErrorMessage = "Очакваният изход не може да бъде по-дълъг от 10000 символа")]
        [Display(Name = "Очакван изход")]
        public string Expected { get; set; } = string.Empty;

        /// <summary>Shown to the student in the question as an example of input and output.</summary>
        public bool IsExample { get; set; }

        /// <summary>The student sees only whether the test passed, never its input or expected output.</summary>
        public bool IsHidden { get; set; }

        /// <summary>The test's share of the question's points relative to the other tests (Moodle <c>mark</c>).</summary>
        [Range(0d, 1000d, ErrorMessage = "Тежестта трябва да е между 0 и 1000")]
        public decimal Weight { get; set; } = 1m;

        /// <summary>Order of the test within the question.</summary>
        public int Order { get; set; }

        public int QuestionId { get; set; }
        public Question Question { get; set; } = null!;
    }
}
