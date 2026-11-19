using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// One line in a quiz's composition. It either pins a specific bank question
    /// (<see cref="QuizItemKind.FixedQuestion"/>) or draws a number of random questions from a section
    /// (<see cref="QuizItemKind.RandomFromSection"/>) when the student starts.
    /// </summary>
    public class QuizItem
    {
        public int Id { get; set; }

        public int QuizId { get; set; }
        public Quiz Quiz { get; set; } = null!;

        /// <summary>Position of the item within the quiz.</summary>
        public int Order { get; set; }

        public QuizItemKind Kind { get; set; }

        /// <summary>The pinned question, for <see cref="QuizItemKind.FixedQuestion"/>.</summary>
        public int? QuestionId { get; set; }
        public Question? Question { get; set; }

        /// <summary>The section to draw from, for <see cref="QuizItemKind.RandomFromSection"/>.</summary>
        public int? SectionId { get; set; }
        public Section? Section { get; set; }

        /// <summary>How many questions to draw, for <see cref="QuizItemKind.RandomFromSection"/>.</summary>
        public int? PickCount { get; set; }
    }
}
