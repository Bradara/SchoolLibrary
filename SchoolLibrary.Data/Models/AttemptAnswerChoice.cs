namespace SchoolLibrary.Data.Models
{
    /// <summary>
    /// One part of a student's answer that refers to an option: a ticked option of a multiple-choice
    /// question, a pair of a matching question (<see cref="MatchedOptionId"/>), or an item's place in an
    /// ordering question (<see cref="Position"/>).
    /// </summary>
    public class AttemptAnswerChoice
    {
        public int Id { get; set; }

        public int AttemptAnswerId { get; set; }
        public AttemptAnswer AttemptAnswer { get; set; } = null!;

        /// <summary>The ticked option, or the left side of a matching pair, or the placed item.</summary>
        public int OptionId { get; set; }
        public AnswerOption Option { get; set; } = null!;

        /// <summary>The option chosen as the match of <see cref="OptionId"/> (matching questions).</summary>
        public int? MatchedOptionId { get; set; }
        public AnswerOption? MatchedOption { get; set; }

        /// <summary>The position the student gave <see cref="OptionId"/> (ordering questions).</summary>
        public int? Position { get; set; }
    }
}
