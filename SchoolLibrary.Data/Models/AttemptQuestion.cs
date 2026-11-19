namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// A question served to one attempt: the per-attempt snapshot taken when the student starts, after
    /// the quiz's items are resolved (so random draws stay fixed for grading and review).
    /// </summary>
    public class AttemptQuestion
    {
        public int Id { get; set; }

        public int AttemptId { get; set; }
        public QuizAttempt Attempt { get; set; } = null!;

        public int QuestionId { get; set; }
        public Question Question { get; set; } = null!;

        /// <summary>Position of the question within the attempt.</summary>
        public int Order { get; set; }
    }
}
