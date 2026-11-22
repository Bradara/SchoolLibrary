using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// "Провери" on a code question: runs the program before the quiz is submitted and tells the student how the
/// tests went. In an exam every check is stored and the wrong ones take points off the question later.
/// </summary>
public interface ICodeCheckService
{
    /// <summary>Checks a program while practising. Nothing is stored: the page keeps the count of wrong checks.</summary>
    Task<ServiceResult<CodeCheckResult>> CheckPracticeAsync(int quizId, int questionId, string? code);

    /// <summary>Checks a program during the exam attempt with <paramref name="token"/> and stores the check.</summary>
    Task<ServiceResult<CodeCheckResult>> CheckExamAsync(Guid token, int questionId, string? code);
}

/// <summary>One test as the student sees it after a check: a hidden test shows only whether it passed.</summary>
public record CodeCheckTest(bool Passed, bool Hidden, string? Stdin, string? Expected, string? Output, string? Error, bool TimedOut);

/// <summary>
/// The outcome of a check. <paramref name="Counted"/> is false when the program could not be judged (the server
/// was not reached), so the check neither helps nor hurts. <paramref name="WrongChecks"/> and
/// <paramref name="ChecksLeft"/> are known only in an exam.
/// </summary>
public record CodeCheckResult(CodeGradeStatus Status, string? Message, bool Passed, bool Counted,
    IReadOnlyList<CodeCheckTest> Tests, int PenaltyPercent, decimal PenaltyPointsPerCheck, int? WrongChecks = null, int? ChecksLeft = null);
