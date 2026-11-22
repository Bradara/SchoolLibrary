namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// One press of "Провери" on a code question during an exam: the program was run before the exam was
    /// submitted. Wrong checks (not every test passed) take points off the question when it is graded.
    /// </summary>
    public class CodeCheck
    {
        public int Id { get; set; }

        public int AttemptId { get; set; }
        public QuizAttempt Attempt { get; set; } = null!;

        public int QuestionId { get; set; }
        public Question Question { get; set; } = null!;

        /// <summary>When the program was checked (UTC).</summary>
        public DateTime CheckedOn { get; set; }

        /// <summary>The program did not pass every test (or did not compile), so the check counts against the student.</summary>
        public bool IsWrong { get; set; }
    }
}
