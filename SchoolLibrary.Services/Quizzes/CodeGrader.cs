using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.CodeRunning;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>How grading a code answer ended.</summary>
public enum CodeGradeStatus
{
    /// <summary>The program was run with every test; the points follow from the passed tests.</summary>
    Graded,

    /// <summary>Nothing was written: no points, the server is not asked.</summary>
    Unanswered,

    /// <summary>The code is longer than <see cref="CodeGrader.MaxCodeLength"/>: no points, the server is not asked.</summary>
    TooLong,

    /// <summary>The program did not compile: no points, every test failed.</summary>
    CompileError,

    /// <summary>The whole run used up its time or memory on the server: no points, every test failed.</summary>
    LimitExceeded,

    /// <summary>The server could not check the program (unreachable, overloaded, a broken question). Grade it again later.</summary>
    NotChecked,
}

/// <summary>The result of one test case.</summary>
/// <param name="Output">What the program printed (cut by the server when very long).</param>
/// <param name="Error">The error output, with mono's repeated exception left out.</param>
public record CodeTestResult(CodeTestCase Test, bool Passed, string Output, string Error, bool TimedOut);

/// <summary>The outcome of grading a code answer.</summary>
/// <param name="Code">The code as accepted (white space at the end removed).</param>
/// <param name="Message">The compiler's message, or why the answer was not checked.</param>
public record CodeGrade(CodeGradeStatus Status, decimal AwardedPoints, bool IsCorrect, string Code,
    IReadOnlyList<CodeTestResult> Tests, string? Message = null)
{
    /// <summary>Whether the grade is final; when false the answer has to be checked again.</summary>
    public bool IsFinal => Status != CodeGradeStatus.NotChecked;
}

/// <summary>
/// Grades a code question: the program runs on the sandbox server with every test's input and passes a test
/// when it ends normally, in time, with the expected output. The points are all or nothing (as CodeRunner by
/// default) or the share of the passed tests' weights.
/// </summary>
public sealed class CodeGrader
{
    /// <summary>The longest program that is checked (the size of the answer column).</summary>
    public const int MaxCodeLength = 10000;

    private readonly ICodeRunner _runner;

    public CodeGrader(ICodeRunner runner) => _runner = runner;

    public async Task<CodeGrade> GradeAsync(Question question, string? code, bool allOrNothing, CancellationToken cancellationToken = default)
    {
        if (question.Type != QuestionType.Code)
        {
            throw new ArgumentException("Not a code question.", nameof(question));
        }

        var accepted = (code ?? string.Empty).TrimEnd();
        var tests = question.TestCases.OrderBy(t => t.Order).ThenBy(t => t.Id).ToList();

        if (accepted.Trim().Length == 0)
        {
            return Failed(CodeGradeStatus.Unanswered, string.Empty, tests);
        }

        if (accepted.Length > MaxCodeLength)
        {
            return Failed(CodeGradeStatus.TooLong, accepted, tests, $"Кодът е по-дълъг от {MaxCodeLength} символа.");
        }

        if (!CodeLanguages.IsSupported(question.CodeLanguage))
        {
            return new CodeGrade(CodeGradeStatus.NotChecked, 0m, false, accepted, [], "Въпросът няма поддържан език.");
        }

        if (tests.Count == 0)
        {
            return new CodeGrade(CodeGradeStatus.NotChecked, 0m, false, accepted, [], "Въпросът няма тестове.");
        }

        var run = await _runner.RunAsync(question.CodeLanguage!, accepted, tests.Select(t => t.Stdin).ToList(), cancellationToken);

        switch (run.Status)
        {
            case CodeRunStatus.CompileError:
                return Failed(CodeGradeStatus.CompileError, accepted, tests, run.Message);
            case CodeRunStatus.TimeLimitExceeded:
                return Failed(CodeGradeStatus.LimitExceeded, accepted, tests, "Програмата надвиши времето за изпълнение.");
            case CodeRunStatus.MemoryLimitExceeded:
                return Failed(CodeGradeStatus.LimitExceeded, accepted, tests, "Програмата надвиши позволената памет.");
            case CodeRunStatus.Unavailable:
                return new CodeGrade(CodeGradeStatus.NotChecked, 0m, false, accepted, [], "Сървърът за проверка не е достъпен.");
        }

        var results = tests.Select((test, i) =>
        {
            var output = i < run.Tests.Count ? run.Tests[i] : null; // a missing result counts as failed
            var passed = output is { ExitCode: 0, TimedOut: false } && CodeOutput.Matches(test.Expected, output.Stdout);
            return new CodeTestResult(test, passed, output?.Stdout ?? string.Empty,
                CodeOutput.ShortenError(output?.Stderr), output?.TimedOut ?? false);
        }).ToList();

        var allPassed = results.All(r => r.Passed);
        var share = allOrNothing ? (allPassed ? 1m : 0m) : PassedShare(results);
        var points = Math.Round(question.Points * share, 2, MidpointRounding.AwayFromZero);
        return new CodeGrade(CodeGradeStatus.Graded, points, allPassed, accepted, results);
    }

    // The passed tests' share of the total weight; with every weight 0 each test counts the same
    private static decimal PassedShare(IReadOnlyList<CodeTestResult> results)
    {
        var total = results.Sum(r => r.Test.Weight);
        return total > 0
            ? results.Where(r => r.Passed).Sum(r => r.Test.Weight) / total
            : (decimal)results.Count(r => r.Passed) / results.Count;
    }

    private static CodeGrade Failed(CodeGradeStatus status, string code, IEnumerable<CodeTestCase> tests, string? message = null)
        => new(status, 0m, false, code,
            tests.Select(t => new CodeTestResult(t, false, string.Empty, string.Empty, false)).ToList(), message);
}
