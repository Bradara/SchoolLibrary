using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>What a teacher types in the quiz form. The exam code is generated, not entered.</summary>
public record QuizInput(string Title, string? Description, QuizMode Mode, int? TimeLimitMinutes, int? ResourceId, int? MaxLeaves = null,
    bool ShuffleQuestions = true, bool ShuffleOptions = true, bool OneQuestionPerPage = true);

/// <summary>
/// A question with its options, which one is correct (by index in <paramref name="Options"/>), how many
/// points it is worth and its type. Multiple-choice, short-answer and numerical questions set
/// <paramref name="Fractions"/> (each option's share of the points, -100 to 100) instead of
/// <paramref name="CorrectIndex"/>. For a short-answer question the options are the accepted answers and
/// <paramref name="CaseSensitive"/> applies; for a numerical question they are the accepted values (or "*")
/// and <paramref name="Tolerances"/> gives each one's tolerance (null where the typed tolerance is not a number).
/// For a matching question the options are the items on the left (empty for an extra wrong answer) and
/// <paramref name="Matches"/> the answers on the right that go with them.
/// When an existing question is edited, <paramref name="OptionIds"/> gives the id of the option each row
/// edits (null for a new row), so an answered question keeps its options and the students' answers.
/// </summary>
public record QuestionInput(string Text, IReadOnlyList<string> Options, int CorrectIndex, int Points = 1,
    QuestionType Type = QuestionType.SingleChoice, IReadOnlyList<decimal>? Fractions = null, bool CaseSensitive = false,
    IReadOnlyList<decimal?>? Tolerances = null, IReadOnlyList<string>? Matches = null, IReadOnlyList<int?>? OptionIds = null)
{
    /// <summary>A code question's settings and tests; null for the other types.</summary>
    public CodeQuestionInput? Code { get; init; }

    /// <summary>
    /// A code question in <paramref name="language"/> (a <c>CodeLanguages</c> id) whose answer box starts with
    /// <paramref name="preload"/>, checked with <paramref name="tests"/>.
    /// </summary>
    public static QuestionInput CodeQuestion(string text, string language, IReadOnlyList<CodeTestInput> tests,
        string? preload = null, bool allOrNothing = true, int points = 1, string? statement = null, int checkPenaltyPercent = 10)
        => new(text, Array.Empty<string>(), -1, points, QuestionType.Code)
        {
            Code = new CodeQuestionInput(language, preload, allOrNothing, tests, statement, checkPenaltyPercent)
        };

    /// <summary>The option texts of a true/false question, in their fixed order.</summary>
    public static readonly IReadOnlyList<string> TrueFalseOptions = new[] { "Вярно", "Невярно" };

    /// <summary>
    /// A true/false question. <paramref name="answer"/> is the correct value; null leaves no option marked,
    /// which validation rejects.
    /// </summary>
    public static QuestionInput TrueFalse(string text, bool? answer, int points = 1)
        => new(text, TrueFalseOptions, answer switch { true => 0, false => 1, null => -1 }, points, QuestionType.TrueFalse);

    /// <summary>
    /// A multiple-choice question whose correct options (<paramref name="correctIndexes"/>) share the points
    /// equally and whose wrong options take away the same share, so ticking everything earns nothing.
    /// With no correct option every share is 0, which validation rejects.
    /// </summary>
    public static QuestionInput MultipleChoice(string text, IReadOnlyList<string> options, IEnumerable<int> correctIndexes, int points = 1)
    {
        var correct = correctIndexes.Where(i => i >= 0 && i < options.Count).ToHashSet();
        var share = correct.Count == 0 ? 0m : Math.Round(AnswerOption.FullCredit / correct.Count, 2, MidpointRounding.AwayFromZero);
        var fractions = options.Select((_, i) => correct.Contains(i) ? share : -share).ToList();
        return new QuestionInput(text, options, -1, points, QuestionType.MultipleChoice, fractions);
    }

    /// <summary>A short-answer question where each of <paramref name="acceptedAnswers"/> earns all the points.</summary>
    public static QuestionInput ShortAnswer(string text, IReadOnlyList<string> acceptedAnswers, bool caseSensitive = false, int points = 1)
        => new(text, acceptedAnswers, -1, points, QuestionType.ShortAnswer,
            acceptedAnswers.Select(_ => AnswerOption.FullCredit).ToList(), caseSensitive);

    /// <summary>
    /// A numerical question where each of <paramref name="values"/> (a number with a comma or point, or "*")
    /// earns all the points within its tolerance.
    /// </summary>
    public static QuestionInput Numerical(string text, IReadOnlyList<string> values, IReadOnlyList<decimal?> tolerances, int points = 1)
        => new(text, values, -1, points, QuestionType.Numerical,
            values.Select(_ => AnswerOption.FullCredit).ToList(), Tolerances: tolerances);

    /// <summary>
    /// A matching question from (item, answer) pairs. A pair with an empty item is an extra wrong answer that
    /// only appears in the list on the right.
    /// </summary>
    public static QuestionInput Matching(string text, IReadOnlyList<(string Item, string Answer)> pairs, int points = 1)
        => new(text, pairs.Select(p => p.Item).ToList(), -1, points, QuestionType.Matching,
            Matches: pairs.Select(p => p.Answer).ToList());

    /// <summary>A description: text only, no options and no points.</summary>
    public static QuestionInput Description(string text)
        => new(text, Array.Empty<string>(), -1, 0, QuestionType.Description);
}

/// <summary>
/// A code answer that has already been run (or read back from an attempt): the program as accepted, the
/// points it earned and the stored results.
/// </summary>
/// <param name="Pending">The program waits to be checked in the background; its points are not known yet.</param>
public record CodeOutcome(string Code, decimal AwardedPoints, bool IsCorrect, StoredCodeResults? Results, bool Pending = false);

/// <summary>What a teacher sets for a code question: language, starting code, scoring and the tests.</summary>
/// <param name="Statement">The full task, several lines long; the question's text is its short title.</param>
/// <param name="CheckPenaltyPercent">The share of the points taken off for every wrong check (0–100).</param>
public record CodeQuestionInput(string Language, string? Preload, bool AllOrNothing, IReadOnlyList<CodeTestInput> Tests,
    string? Statement = null, int CheckPenaltyPercent = 10);

/// <summary>One test of a code question as typed in the form.</summary>
public record CodeTestInput(string Stdin, string Expected, bool IsExample = false, bool IsHidden = false, decimal Weight = 1m);

/// <summary>A quiz with its question and (submitted) attempt counts, for the management list.</summary>
public record QuizListItem(Quiz Quiz, int QuestionCount, int AttemptCount);

/// <summary>How one question was answered: the accepted answer, the points it earned and the outcome.</summary>
public record QuestionResult(Question Question, SubmittedAnswer Answer, decimal AwardedPoints, bool IsCorrect)
{
    /// <summary>For a code question: how the program ran (status, compiler message, every test); null when unknown.</summary>
    public StoredCodeResults? CodeResults { get; init; }

    /// <summary>Whether this is a code question.</summary>
    public bool IsCode => Question.Type == QuestionType.Code;

    /// <summary>For a code question: the program still waits to be checked, so its points are not counted yet.</summary>
    public bool CodePending { get; init; }

    /// <summary>The picked option of a single-choice question; null when unanswered.</summary>
    public int? SelectedOptionId => Answer.OptionId;

    /// <summary>The option worth full credit, for showing the right answer.</summary>
    public int? CorrectOptionId => Question.Options.FirstOrDefault(o => o.IsCorrect)?.Id;

    /// <summary>Whether this is a description (text only) rather than a question.</summary>
    public bool IsDescription => Question.Type == QuestionType.Description;

    /// <summary>Whether the answer earned some, but not all, of the points.</summary>
    public bool IsPartial => !IsCorrect && AwardedPoints > 0;

    /// <summary>What the student typed (short answer); null when nothing was typed.</summary>
    public string? TypedText => Answer.Text;

    /// <summary>The answer picked for a matching item; null when none was picked.</summary>
    public string? PickedMatch(int itemOptionId) => Answer.MatchedPairs.TryGetValue(itemOptionId, out var text) ? text : null;

    /// <summary>Whether the student picked or ticked <paramref name="optionId"/>.</summary>
    public bool IsChosen(int optionId) => Answer.OptionId == optionId || Answer.TickedOptionIds.Contains(optionId);

    /// <summary>Whether <paramref name="option"/> earns credit, i.e. is (one of) the right answers.</summary>
    public static bool Credits(AnswerOption option) => option.Fraction > 0;
}

/// <summary>The graded outcome of a quiz: the score, grade and the per-question breakdown.</summary>
public record QuizResult(Quiz Quiz, decimal Score, decimal MaxScore, IReadOnlyList<QuestionResult> Questions)
{
    /// <summary>The score as a whole-number percentage of the maximum.</summary>
    public int Percent => QuizGradeScale.Percent(Score, MaxScore);

    /// <summary>The 2–6 grade for the score.</summary>
    public int Grade => QuizGradeScale.Grade(Score, MaxScore);

    /// <summary>The Bulgarian label for <see cref="Grade"/> (Отличен, Много добър, …).</summary>
    public string GradeLabel => QuizGradeScale.Label(Grade);

    /// <summary>Whether some programs still wait to be checked, so the score may still go up.</summary>
    public bool HasPendingCode => Questions.Any(q => q.CodePending);
}

/// <summary>A practice quiz with the concrete questions resolved for this run (random draws fixed).</summary>
public record PracticeSet(Quiz Quiz, IReadOnlyList<Question> Questions);

/// <summary>A quiz with one page of its submitted attempts, for the teacher's review page.</summary>
public record QuizReview(Quiz Quiz, PagedResult<QuizAttempt> Attempts);

/// <summary>
/// The exam behind a code, for the second entry step (name and number). <paramref name="ClassName"/> is
/// null for an exam-mode quiz; <paramref name="IsOpen"/> is false when a class exam accepts no new attempts.
/// </summary>
public record ExamEntry(string Code, string Title, string? ClassName, int? TimeLimitMinutes, bool IsOpen);

/// <summary>Proctoring state after a student left the exam: the leave count and whether to submit now.</summary>
public record ProctorStatus(int LeaveCount, bool Flagged, bool ShouldSubmit);
