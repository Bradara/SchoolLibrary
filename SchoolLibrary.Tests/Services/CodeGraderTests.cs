using Moq;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.CodeRunning;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class CodeGraderTests
{
    private const string Code = "print(sum(map(int, input().split())))";

    private static Question CodeQuestion(int points = 4, string? language = CodeLanguages.Python, params CodeTestCase[] tests) => new()
    {
        Id = 7,
        Text = "Сума",
        Type = QuestionType.Code,
        Points = points,
        CodeLanguage = language,
        TestCases = tests.Length > 0
            ? tests.ToList()
            :
            [
                new CodeTestCase { Id = 1, Stdin = "2 3", Expected = "5", Order = 0 },
                new CodeTestCase { Id = 2, Stdin = "-10 4", Expected = "-6", Order = 1 },
                new CodeTestCase { Id = 3, Stdin = "0 0", Expected = "0", Order = 2, Weight = 2 },
            ],
    };

    private static CodeTestOutput Ok(string stdout) => new(stdout, "", 0, false);

    private static (CodeGrader Grader, Mock<ICodeRunner> Runner) Create(CodeRunResult result)
    {
        var runner = new Mock<ICodeRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return (new CodeGrader(runner.Object), runner);
    }

    [Fact]
    public async Task AllTestsPass_FullPoints()
    {
        var (grader, runner) = Create(new CodeRunResult(CodeRunStatus.Completed, [Ok("5\n"), Ok("-6"), Ok("0  \n\n")]));

        var grade = await grader.GradeAsync(CodeQuestion(), Code + "\n\n", allOrNothing: true);

        Assert.Equal(CodeGradeStatus.Graded, grade.Status);
        Assert.Equal(4m, grade.AwardedPoints);
        Assert.True(grade.IsCorrect);
        Assert.True(grade.IsFinal);
        Assert.Equal(Code, grade.Code);
        Assert.All(grade.Tests, t => Assert.True(t.Passed));
        runner.Verify(r => r.RunAsync("python3", Code,
            It.Is<IReadOnlyList<string>>(s => s.SequenceEqual(new[] { "2 3", "-10 4", "0 0" })), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task RunsTheTestsInTheirOrder()
    {
        var question = CodeQuestion(2, CodeLanguages.CSharp,
            new CodeTestCase { Id = 5, Stdin = "b", Expected = "B", Order = 1 },
            new CodeTestCase { Id = 9, Stdin = "a", Expected = "A", Order = 0 });
        var (grader, runner) = Create(new CodeRunResult(CodeRunStatus.Completed, [Ok("A"), Ok("B")]));

        var grade = await grader.GradeAsync(question, "class P {}", allOrNothing: true);

        Assert.True(grade.IsCorrect);
        Assert.Equal(new[] { 9, 5 }, grade.Tests.Select(t => t.Test.Id));
        runner.Verify(r => r.RunAsync("csharp", "class P {}",
            It.Is<IReadOnlyList<string>>(s => s.SequenceEqual(new[] { "a", "b" })), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task AllOrNothing_OneFailedTest_NoPoints()
    {
        var (grader, _) = Create(new CodeRunResult(CodeRunStatus.Completed, [Ok("5"), Ok("6"), Ok("0")]));

        var grade = await grader.GradeAsync(CodeQuestion(), Code, allOrNothing: true);

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
        Assert.Equal(new[] { true, false, true }, grade.Tests.Select(t => t.Passed));
        Assert.Equal("6", grade.Tests[1].Output);
    }

    [Fact]
    public async Task Partial_PointsFollowTheWeightsOfThePassedTests()
    {
        // weights 1, 1, 2: the first and the last pass → 3 of 4 → 3 of 4 points
        var (grader, _) = Create(new CodeRunResult(CodeRunStatus.Completed, [Ok("5"), Ok("6"), Ok("0")]));

        var grade = await grader.GradeAsync(CodeQuestion(), Code, allOrNothing: false);

        Assert.Equal(3m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
    }

    [Fact]
    public async Task Partial_RoundsToTwoDecimals()
    {
        var question = CodeQuestion(1, CodeLanguages.Python,
            new CodeTestCase { Stdin = "1", Expected = "1", Order = 0 },
            new CodeTestCase { Stdin = "2", Expected = "2", Order = 1 },
            new CodeTestCase { Stdin = "3", Expected = "3", Order = 2 });
        var (grader, _) = Create(new CodeRunResult(CodeRunStatus.Completed, [Ok("1"), Ok("x"), Ok("x")]));

        var grade = await grader.GradeAsync(question, Code, allOrNothing: false);

        Assert.Equal(0.33m, grade.AwardedPoints);
    }

    [Fact]
    public async Task Partial_AllWeightsZero_EveryTestCountsTheSame()
    {
        var question = CodeQuestion(2, CodeLanguages.Python,
            new CodeTestCase { Stdin = "1", Expected = "1", Order = 0, Weight = 0 },
            new CodeTestCase { Stdin = "2", Expected = "2", Order = 1, Weight = 0 });
        var (grader, _) = Create(new CodeRunResult(CodeRunStatus.Completed, [Ok("1"), Ok("x")]));

        var grade = await grader.GradeAsync(question, Code, allOrNothing: false);

        Assert.Equal(1m, grade.AwardedPoints);
    }

    [Fact]
    public async Task RightOutputButCrashed_Fails()
    {
        var (grader, _) = Create(new CodeRunResult(CodeRunStatus.Completed,
            [Ok("5"), new CodeTestOutput("-6", "Traceback", 1, false), Ok("0")]));

        var grade = await grader.GradeAsync(CodeQuestion(), Code, allOrNothing: true);

        Assert.False(grade.Tests[1].Passed);
        Assert.Equal("Traceback", grade.Tests[1].Error);
    }

    [Fact]
    public async Task TimedOutTest_Fails()
    {
        var (grader, _) = Create(new CodeRunResult(CodeRunStatus.Completed,
            [Ok("5"), Ok("-6"), new CodeTestOutput("0", "", -1, true)]));

        var grade = await grader.GradeAsync(CodeQuestion(), Code, allOrNothing: false);

        Assert.False(grade.Tests[2].Passed);
        Assert.True(grade.Tests[2].TimedOut);
        Assert.Equal(2m, grade.AwardedPoints); // weights 1 + 1 of 4
    }

    [Fact]
    public async Task MissingResults_CountAsFailed()
    {
        var (grader, _) = Create(new CodeRunResult(CodeRunStatus.Completed, [Ok("5")]));

        var grade = await grader.GradeAsync(CodeQuestion(), Code, allOrNothing: false);

        Assert.Equal(new[] { true, false, false }, grade.Tests.Select(t => t.Passed));
        Assert.Equal(1m, grade.AwardedPoints);
    }

    [Fact]
    public async Task MonoErrorIsShortened()
    {
        const string stderr = "\nUnhandled Exception:\nSystem.FormatException: bad\n[ERROR] FATAL UNHANDLED EXCEPTION: System.FormatException: bad\n";
        var (grader, _) = Create(new CodeRunResult(CodeRunStatus.Completed,
            [new CodeTestOutput("", stderr, 1, false), Ok("-6"), Ok("0")]));

        var grade = await grader.GradeAsync(CodeQuestion(), Code, allOrNothing: true);

        Assert.Equal("Unhandled Exception:\nSystem.FormatException: bad", grade.Tests[0].Error);
    }

    [Fact]
    public async Task CompileError_NoPoints_AllTestsFailed_WithTheMessage()
    {
        var (grader, _) = Create(CodeRunResult.Failed(CodeRunStatus.CompileError, "Program.cs(1,7): error CS1001"));

        var grade = await grader.GradeAsync(CodeQuestion(), "class {", allOrNothing: false);

        Assert.Equal(CodeGradeStatus.CompileError, grade.Status);
        Assert.Equal(0m, grade.AwardedPoints);
        Assert.True(grade.IsFinal);
        Assert.Equal("Program.cs(1,7): error CS1001", grade.Message);
        Assert.Equal(3, grade.Tests.Count);
        Assert.All(grade.Tests, t => Assert.False(t.Passed));
    }

    [Theory]
    [InlineData(CodeRunStatus.TimeLimitExceeded, "времето")]
    [InlineData(CodeRunStatus.MemoryLimitExceeded, "памет")]
    public async Task WholeRunOverTheLimit_NoPoints(CodeRunStatus status, string messagePart)
    {
        var (grader, _) = Create(CodeRunResult.Failed(status));

        var grade = await grader.GradeAsync(CodeQuestion(), Code, allOrNothing: false);

        Assert.Equal(CodeGradeStatus.LimitExceeded, grade.Status);
        Assert.Equal(0m, grade.AwardedPoints);
        Assert.Contains(messagePart, grade.Message);
        Assert.True(grade.IsFinal);
    }

    [Fact]
    public async Task ServerUnavailable_NotChecked_AndNotFinal()
    {
        var (grader, _) = Create(CodeRunResult.Failed(CodeRunStatus.Unavailable, "Connection refused"));

        var grade = await grader.GradeAsync(CodeQuestion(), Code, allOrNothing: true);

        Assert.Equal(CodeGradeStatus.NotChecked, grade.Status);
        Assert.False(grade.IsFinal);
        Assert.Equal(Code, grade.Code);
        Assert.Empty(grade.Tests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n\t ")]
    public async Task Unanswered_NoPoints_ServerNotAsked(string? code)
    {
        var (grader, runner) = Create(new CodeRunResult(CodeRunStatus.Completed, []));

        var grade = await grader.GradeAsync(CodeQuestion(), code, allOrNothing: true);

        Assert.Equal(CodeGradeStatus.Unanswered, grade.Status);
        Assert.Equal(0m, grade.AwardedPoints);
        Assert.Equal("", grade.Code);
        Assert.True(grade.IsFinal);
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TooLongCode_NoPoints_ServerNotAsked_CodeKeptWhole()
    {
        var (grader, runner) = Create(new CodeRunResult(CodeRunStatus.Completed, []));
        var code = new string('x', CodeGrader.MaxCodeLength + 1);

        var grade = await grader.GradeAsync(CodeQuestion(), code, allOrNothing: true);

        Assert.Equal(CodeGradeStatus.TooLong, grade.Status);
        Assert.Equal(code, grade.Code);
        runner.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("rust")]
    public async Task UnsupportedLanguage_NotChecked(string? language)
    {
        var (grader, runner) = Create(new CodeRunResult(CodeRunStatus.Completed, []));

        var grade = await grader.GradeAsync(CodeQuestion(language: language), Code, allOrNothing: true);

        Assert.Equal(CodeGradeStatus.NotChecked, grade.Status);
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task QuestionWithoutTests_NotChecked()
    {
        var question = CodeQuestion();
        question.TestCases.Clear();
        var (grader, runner) = Create(new CodeRunResult(CodeRunStatus.Completed, []));

        var grade = await grader.GradeAsync(question, Code, allOrNothing: true);

        Assert.Equal(CodeGradeStatus.NotChecked, grade.Status);
        Assert.Equal("Въпросът няма тестове.", grade.Message);
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NotACodeQuestion_Throws()
    {
        var (grader, _) = Create(new CodeRunResult(CodeRunStatus.Completed, []));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            grader.GradeAsync(new Question { Type = QuestionType.ShortAnswer }, Code, allOrNothing: true));
    }

    // ---------- CodeOutput ----------

    [Theory]
    [InlineData("5", "5")]
    [InlineData("5\n", "5")]
    [InlineData("5\r\n6\r\n", "5\n6")]
    [InlineData("5   \n6\t\n\n\n", "5\n6")]
    [InlineData("a\n\nb", "a\n\nb")] // blank lines inside are kept
    [InlineData("  a", "  a")]       // leading white space counts
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalize_RemovesTrailingWhiteSpaceOnly(string? output, string expected)
    {
        Assert.Equal(expected, CodeOutput.Normalize(output));
    }

    [Theory]
    [InlineData("Готово", "Готово\n", true)]
    [InlineData("Hello", "hello", false)]
    [InlineData("1 2", "1  2", false)]
    [InlineData("a\nb", "a\r\nb\r\n", true)]
    public void Matches_ComparesNormalizedOutput(string expected, string actual, bool matches)
    {
        Assert.Equal(matches, CodeOutput.Matches(expected, actual));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  Traceback\n", "Traceback")]
    [InlineData("[ERROR] FATAL UNHANDLED EXCEPTION: x", "[ERROR] FATAL UNHANDLED EXCEPTION: x")] // nothing before the marker: kept
    public void ShortenError(string? error, string expected)
    {
        Assert.Equal(expected, CodeOutput.ShortenError(error));
    }
}
