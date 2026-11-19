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

        public bool IsCorrect { get; set; }
    }
}
