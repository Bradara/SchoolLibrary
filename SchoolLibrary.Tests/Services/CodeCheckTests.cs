using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.CodeRunning;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.Tests.Web;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Services;

/// <summary>"Провери" on code questions and the penalty for wrong checks (code questions only).</summary>
public class CodeCheckTests
{
    private const string Owner = "teacher-1";
    private const string Program = "print(sum(map(int, input().split())))";

    private static readonly CodeTestInput[] Tests =
    {
        new("2 3", "5", IsExample: true),
        new("-10 4", "-6", IsHidden: true),
    };

    private static Mock<ICodeRunner> Runner(params string[] outputs) => Runner(
        new CodeRunResult(CodeRunStatus.Completed, outputs.Select(o => new CodeTestOutput(o, "", 0, false)).ToList()));

    private static Mock<ICodeRunner> Runner(CodeRunResult result)
    {
        var runner = new Mock<ICodeRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return runner;
    }

    // A published quiz (practice or exam) with a single-choice question and a code question worth 4 points, 10% per wrong check
    private static async Task<(QuizService Service, Quiz Quiz, Question Choice, Question Code)> QuizAsync(
        Data.ApplicationDbContext context, QuizMode mode, Mock<ICodeRunner> runner, int penalty = 10, CodeGradingSignal? queue = null)
    {
        var service = new QuizService(context, new QuizItemService(context), new CodeGrader(runner.Object), queue);
        var quiz = (await service.CreateAsync(new QuizInput("Програмиране", null, mode, mode == QuizMode.Exam ? 30 : null, null), Owner)).Value!;
        var choice = (await service.AddQuestionAsync(quiz.Id, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0), Owner, false)).Value!;
        var code = (await service.AddQuestionAsync(quiz.Id, QuestionInput.CodeQuestion("Сбор", CodeLanguages.Python, Tests,
            points: 4, checkPenaltyPercent: penalty), Owner, false)).Value!;
        await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        return (service, (await service.GetByIdAsync(quiz.Id))!, choice, code);
    }

    private static IReadOnlyDictionary<int, SubmittedAnswer> Answers(Question code, string program = Program)
        => SubmittedAnswer.FromForm(null, null, null, null, new Dictionary<int, string?> { [code.Id] = program });

    // ---------- Penalty ----------

    private static Question Penalized(int points = 4, int percent = 10)
        => new() { Type = QuestionType.Code, Points = points, CheckPenaltyPercent = percent };

    [Theory]
    [InlineData(4, 10, 0, 0)]
    [InlineData(4, 10, 2, 0.8)]
    [InlineData(3, 15, 1, 0.45)]
    [InlineData(4, 10, 100, 12)] // counted up to the most checks allowed (30)
    [InlineData(4, 0, 5, 0)]
    public void Penalty_For_IsThePercentOfThePointsPerWrongCheck(int points, int percent, int wrong, double expected)
    {
        Assert.Equal((decimal)expected, CodePenalty.For(Penalized(points, percent), wrong));
    }

    [Fact]
    public void Penalty_Apply_TakesPointsOff_NeverBelowZero_AndIsNoLongerFullCredit()
    {
        var question = Penalized();
        var full = new CodeGrade(CodeGradeStatus.Graded, 4m, true, Program, []);

        var twoWrong = CodePenalty.Apply(full, question, 2);
        Assert.Equal(3.2m, twoWrong.AwardedPoints);
        Assert.False(twoWrong.IsCorrect);
        Assert.Equal((2, 0.8m), (twoWrong.Results!.WrongChecks, twoWrong.Results.PenaltyPoints));

        var many = CodePenalty.Apply(new CodeGrade(CodeGradeStatus.Graded, 1m, false, Program, []), question, 20);
        Assert.Equal(0m, many.AwardedPoints);
        Assert.Equal(1m, many.Results!.PenaltyPoints); // only what was earned is taken

        var none = CodePenalty.Apply(full, question, 0);
        Assert.Equal(4m, none.AwardedPoints);
        Assert.True(none.IsCorrect);
    }

    [Fact]
    public void Penalty_Apply_Unanswered_StaysEmpty()
    {
        var outcome = CodePenalty.Apply(new CodeGrade(CodeGradeStatus.Unanswered, 0m, false, "", []), Penalized(), 3);

        Assert.Equal(0m, outcome.AwardedPoints);
        Assert.Null(outcome.Results);
    }

    [Fact]
    public void StoredResults_KeepThePenalty()
    {
        var stored = StoredCodeResults.From(new CodeGrade(CodeGradeStatus.Graded, 4m, true, Program, [])) with { WrongChecks = 2, PenaltyPoints = 0.8m };

        var read = StoredCodeResults.FromJson(stored.ToJson())!;

        Assert.Equal((2, 0.8m), (read.WrongChecks, read.PenaltyPoints));
    }

    // ---------- Settings ----------

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_PenaltyOutOfRange_IsRejected(int percent)
    {
        var input = QuestionInput.CodeQuestion("Сбор", "python3", Tests, checkPenaltyPercent: percent);

        Assert.Equal("Наказанието за грешна проверка трябва да е между 0 и 100%.", QuizService.ValidateQuestion(input));
    }

    [Fact]
    public async Task Penalty_IsStored_AndResetForAnotherType()
    {
        using var context = TestDbContextFactory.Create();
        var (service, _, _, code) = await QuizAsync(context, QuizMode.Practice, Runner(), penalty: 25);
        Assert.Equal(25, code.CheckPenaltyPercent);
        context.ChangeTracker.Clear();

        await service.UpdateQuestionAsync(code.Id, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0), Owner, false);

        Assert.Equal(10, (await service.GetQuestionAsync(code.Id))!.CheckPenaltyPercent);
    }

    [Fact]
    public void Form_CarriesThePenalty()
    {
        var form = QuestionFormViewModel.FromQuestion(new Question
        {
            Type = QuestionType.Code, Text = "Сбор", CodeLanguage = "python3", CheckPenaltyPercent = 0,
            TestCases = { new CodeTestCase { Stdin = "1", Expected = "1" } },
        });

        Assert.Equal(0, form.CheckPenaltyPercent);
        Assert.Equal(0, form.ToInput().Code!.CheckPenaltyPercent);
        Assert.Equal(10, new QuestionFormViewModel().CheckPenaltyPercent);
    }

    // ---------- Check while practising ----------

    [Fact]
    public async Task Practice_Check_ShowsTheTests_HidesTheHiddenOnes_AndStoresNothing()
    {
        using var context = TestDbContextFactory.Create();
        var runner = Runner("5", "0");
        var (_, quiz, _, code) = await QuizAsync(context, QuizMode.Practice, runner);
        var checks = new CodeCheckService(context, new CodeGrader(runner.Object));

        var result = (await checks.CheckPracticeAsync(quiz.Id, code.Id, Program)).Value!;

        Assert.Equal(CodeGradeStatus.Graded, result.Status);
        Assert.False(result.Passed);
        Assert.True(result.Counted);
        Assert.Equal((10, 0.4m), (result.PenaltyPercent, result.PenaltyPointsPerCheck));
        Assert.Null(result.WrongChecks);
        Assert.Equal(new CodeCheckTest(true, false, "2 3", "5", "5", "", false), result.Tests[0]);
        Assert.Equal(new CodeCheckTest(false, true, null, null, null, null, false), result.Tests[1]);
        Assert.Empty(context.CodeChecks);
    }

    [Fact]
    public async Task Practice_Check_QuestionFromTheBank_IsAllowed()
    {
        using var context = TestDbContextFactory.Create();
        var runner = Runner("5", "-6");
        var (_, quiz, _, _) = await QuizAsync(context, QuizMode.Practice, runner);
        var section = new Section { Name = "Програмиране", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();
        var bank = (await new QuestionBankService(context).CreateAsync(section.Id,
            QuestionInput.CodeQuestion("Сбор", "python3", Tests), Owner, false)).Value!;
        context.QuizItems.Add(new QuizItem { QuizId = quiz.Id, Kind = QuizItemKind.RandomFromSection, SectionId = section.Id, PickCount = 1, Order = 1 });
        await context.SaveChangesAsync();
        var checks = new CodeCheckService(context, new CodeGrader(runner.Object));

        var result = await checks.CheckPracticeAsync(quiz.Id, bank.Id, Program);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.Passed);
    }

    [Fact]
    public async Task Practice_Check_OtherQuizOrNotACodeQuestion_IsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var runner = Runner("5", "-6");
        var (_, quiz, choice, code) = await QuizAsync(context, QuizMode.Practice, runner);
        var checks = new CodeCheckService(context, new CodeGrader(runner.Object));

        Assert.Equal(ServiceError.NotFound, (await checks.CheckPracticeAsync(quiz.Id + 1, code.Id, Program)).Error);
        Assert.Equal(ServiceError.NotFound, (await checks.CheckPracticeAsync(quiz.Id, choice.Id, Program)).Error);
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Check_EmptyProgram_OrNoServer_IsRefused()
    {
        using var context = TestDbContextFactory.Create();
        var runner = Runner("5", "-6");
        var (_, quiz, _, code) = await QuizAsync(context, QuizMode.Practice, runner);

        var empty = await new CodeCheckService(context, new CodeGrader(runner.Object)).CheckPracticeAsync(quiz.Id, code.Id, "  ");
        var noServer = await new CodeCheckService(context).CheckPracticeAsync(quiz.Id, code.Id, Program);

        Assert.Equal((ServiceError.Invalid, "Напишете програма преди проверката."), (empty.Error, empty.Message));
        Assert.Equal((ServiceError.Conflict, "Проверката на код не е настроена."), (noServer.Error, noServer.Message));
    }

    [Fact]
    public async Task Practice_Submit_TakesTheWrongChecksFromTheForm()
    {
        using var context = TestDbContextFactory.Create();
        var (service, quiz, choice, code) = await QuizAsync(context, QuizMode.Practice, Runner("5", "-6"));

        var result = (await service.GradePracticeAsync(quiz.Id, new[] { choice.Id, code.Id }, Answers(code),
            new Dictionary<int, int> { [code.Id] = 3, [choice.Id] = 5 })).Value!;

        var codeResult = result.Questions.Single(q => q.IsCode);
        Assert.Equal(2.8m, codeResult.AwardedPoints); // 4 − 3 × 0.4
        Assert.True(codeResult.IsPartial);
        Assert.Equal(3, codeResult.CodeResults!.WrongChecks);
        Assert.Equal(0m, result.Questions.Single(q => !q.IsCode).AwardedPoints); // other questions are not touched (unanswered here)
    }

    // ---------- Check during an exam ----------

    private static async Task<(QuizService Service, CodeCheckService Checks, QuizAttempt Attempt, Question Code)> ExamAsync(
        Data.ApplicationDbContext context, Mock<ICodeRunner> runner, CodeGradingSignal? queue = null)
    {
        var (service, quiz, _, code) = await QuizAsync(context, QuizMode.Exam, runner, queue: queue);
        var attempt = (await service.StartExamAsync(quiz.Code!, "Иван", "5")).Value!;
        return (service, new CodeCheckService(context, new CodeGrader(runner.Object)), attempt, code);
    }

    [Fact]
    public async Task Exam_Checks_AreStoredAndCounted_AndTheSubmitTakesThePenalty()
    {
        using var context = TestDbContextFactory.Create();
        var wrong = Runner("5", "0");
        var (service, _, attempt, code) = await ExamAsync(context, Runner("5", "-6"));
        var wrongChecks = new CodeCheckService(context, new CodeGrader(wrong.Object));

        var first = (await wrongChecks.CheckExamAsync(attempt.Token, code.Id, Program)).Value!;
        var second = (await wrongChecks.CheckExamAsync(attempt.Token, code.Id, Program)).Value!;

        Assert.Equal((1, CodePenalty.MaxChecksPerQuestion - 1), (first.WrongChecks, first.ChecksLeft));
        Assert.Equal((2, CodePenalty.MaxChecksPerQuestion - 2), (second.WrongChecks, second.ChecksLeft));
        Assert.Equal(2, await context.CodeChecks.CountAsync(c => c.IsWrong));

        // The page sees the count again after a reload
        Assert.Equal(2, (await service.GetExamAttemptAsync(attempt.Token))!.CodeChecks.Count);

        var result = (await service.SubmitExamAsync(attempt.Token, Answers(code))).Value!;
        Assert.Equal(3.2m, result.Questions.Single(q => q.IsCode).AwardedPoints);
        Assert.Equal(3.2m, (await context.QuizAttempts.SingleAsync()).Score);
    }

    [Fact]
    public async Task Exam_RightCheck_IsStoredButNotWrong()
    {
        using var context = TestDbContextFactory.Create();
        var (_, checks, attempt, code) = await ExamAsync(context, Runner("5", "-6"));

        var result = (await checks.CheckExamAsync(attempt.Token, code.Id, Program)).Value!;

        Assert.True(result.Passed);
        Assert.Equal(0, result.WrongChecks);
        Assert.False((await context.CodeChecks.SingleAsync()).IsWrong);
    }

    [Fact]
    public async Task Exam_ServerUnavailable_IsNotCounted()
    {
        using var context = TestDbContextFactory.Create();
        var down = Runner(CodeRunResult.Failed(CodeRunStatus.Unavailable));
        var (_, checks, attempt, code) = await ExamAsync(context, down);

        var result = (await checks.CheckExamAsync(attempt.Token, code.Id, Program)).Value!;

        Assert.False(result.Counted);
        Assert.Equal(CodeGradeStatus.NotChecked, result.Status);
        Assert.Contains("не се брои", result.Message);
        Assert.Empty(context.CodeChecks);
    }

    [Fact]
    public async Task Exam_CompileError_CountsAsWrong()
    {
        using var context = TestDbContextFactory.Create();
        var (_, checks, attempt, code) = await ExamAsync(context, Runner(CodeRunResult.Failed(CodeRunStatus.CompileError, "SyntaxError")));

        var result = (await checks.CheckExamAsync(attempt.Token, code.Id, "print(")).Value!;

        Assert.Equal((CodeGradeStatus.CompileError, "SyntaxError", 1), (result.Status, result.Message, result.WrongChecks));
    }

    [Fact]
    public async Task Exam_TooManyChecks_AreRefused()
    {
        using var context = TestDbContextFactory.Create();
        var runner = Runner("5", "0");
        var (_, checks, attempt, code) = await ExamAsync(context, runner);
        for (var i = 0; i < CodePenalty.MaxChecksPerQuestion; i++)
        {
            context.CodeChecks.Add(new CodeCheck { AttemptId = attempt.Id, QuestionId = code.Id, CheckedOn = DateTime.UtcNow, IsWrong = true });
        }

        await context.SaveChangesAsync();
        runner.Invocations.Clear();

        var result = await checks.CheckExamAsync(attempt.Token, code.Id, Program);

        Assert.Equal(ServiceError.Conflict, result.Error);
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Exam_SubmittedOrTimeIsUp_IsRefused()
    {
        using var context = TestDbContextFactory.Create();
        var (service, checks, attempt, code) = await ExamAsync(context, Runner("5", "-6"));
        var stored = await context.QuizAttempts.SingleAsync();
        stored.StartedOn = DateTime.UtcNow.AddMinutes(-40); // 30 minutes allowed
        await context.SaveChangesAsync();

        var late = await checks.CheckExamAsync(attempt.Token, code.Id, Program);
        Assert.Equal((ServiceError.Conflict, "Времето за изпита изтече."), (late.Error, late.Message));

        await service.SubmitExamAsync(attempt.Token, Answers(code));
        var submitted = await checks.CheckExamAsync(attempt.Token, code.Id, Program);
        Assert.Equal((ServiceError.Conflict, "Изпитът вече е предаден."), (submitted.Error, submitted.Message));
    }

    [Fact]
    public async Task Exam_QuestionNotGivenOrUnknownAttempt_IsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var (_, checks, attempt, _) = await ExamAsync(context, Runner("5", "-6"));
        var other = new Question { Text = "Друг", Type = QuestionType.Code, CodeLanguage = "python3" };
        context.Questions.Add(other);
        await context.SaveChangesAsync();

        Assert.Equal(ServiceError.NotFound, (await checks.CheckExamAsync(attempt.Token, other.Id, Program)).Error);
        Assert.Equal(ServiceError.NotFound, (await checks.CheckExamAsync(Guid.NewGuid(), other.Id, Program)).Error);
    }

    [Fact]
    public async Task Exam_BackgroundCheck_TakesThePenaltyToo()
    {
        using var context = TestDbContextFactory.Create();
        var runner = Runner("5", "-6");
        var (service, _, attempt, code) = await ExamAsync(context, runner, new CodeGradingSignal());
        context.CodeChecks.Add(new CodeCheck { AttemptId = attempt.Id, QuestionId = code.Id, CheckedOn = DateTime.UtcNow, IsWrong = true });
        context.CodeChecks.Add(new CodeCheck { AttemptId = attempt.Id, QuestionId = code.Id, CheckedOn = DateTime.UtcNow, IsWrong = false });
        await context.SaveChangesAsync();
        await service.SubmitExamAsync(attempt.Token, Answers(code));
        context.ChangeTracker.Clear();

        await new CodeGradingService(context, new CodeGrader(runner.Object)).GradePendingAsync(4);

        var answer = await context.AttemptAnswers.SingleAsync(a => a.QuestionId == code.Id);
        Assert.Equal(3.6m, answer.AwardedPoints); // one wrong check: 4 − 0.4
        Assert.Equal(1, StoredCodeResults.FromJson(answer.CodeResults)!.WrongChecks);
        Assert.Equal(3.6m, (await context.QuizAttempts.SingleAsync()).Score);
    }

    // ---------- Controller ----------

    private static JsonElement Json(IActionResult result)
        => JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value);

    [Fact]
    public async Task Controller_Check_ReturnsTheResultAsJson()
    {
        var checks = new Mock<ICodeCheckService>();
        checks.Setup(c => c.CheckPracticeAsync(5, 7, "x")).ReturnsAsync(ServiceResult<CodeCheckResult>.Success(
            new CodeCheckResult(CodeGradeStatus.Graded, null, false, true,
                new[] { new CodeCheckTest(false, true, null, null, null, null, false) }, 10, 0.4m)));
        var controller = new QuizzesController(new Mock<IQuizService>().Object).WithUser(null);

        var json = Json(await controller.CheckCode(5, 7, "x", checks.Object));

        Assert.True(json.GetProperty("ok").GetBoolean());
        Assert.Equal("Graded", json.GetProperty("status").GetString());
        Assert.Equal("0,4", json.GetProperty("penaltyPoints").GetString());
        Assert.True(json.GetProperty("tests")[0].GetProperty("Hidden").GetBoolean());
    }

    [Fact]
    public async Task Controller_Check_RefusalAndNotFound()
    {
        var token = Guid.NewGuid();
        var checks = new Mock<ICodeCheckService>();
        checks.Setup(c => c.CheckExamAsync(token, 7, "x"))
            .ReturnsAsync(ServiceResult<CodeCheckResult>.Failure(ServiceError.Conflict, "Изпитът вече е предаден."));
        checks.Setup(c => c.CheckExamAsync(token, 8, "x"))
            .ReturnsAsync(ServiceResult<CodeCheckResult>.Failure(ServiceError.NotFound, "Въпросът не е намерен."));
        var controller = new QuizzesController(new Mock<IQuizService>().Object).WithUser(null);

        var refused = Json(await controller.CheckExamCode(token, 7, "x", checks.Object));
        Assert.False(refused.GetProperty("ok").GetBoolean());
        Assert.Equal("Изпитът вече е предаден.", refused.GetProperty("message").GetString());

        Assert.IsType<NotFoundResult>(await controller.CheckExamCode(token, 8, "x", checks.Object));
    }

    // ---------- Moodle ----------

    private static string CodeRunnerXml(string extra) => $"""
        <quiz><question type="coderunner">
          <name><text>Сбор</text></name>
          <questiontext format="html"><text>Задача</text></questiontext>
          <coderunnertype>python3</coderunnertype><prototypetype>0</prototypetype>
          {extra}
          <testcases><testcase testtype="0" useasexample="0" mark="1.0000000">
            <testcode><text></text></testcode><stdin><text>1</text></stdin><expected><text>1</text></expected>
            <display><text>SHOW</text></display>
          </testcase></testcases>
        </question></quiz>
        """;

    [Theory]
    [InlineData("<penaltyregime>10, 20, ...</penaltyregime>", 10)]
    [InlineData("<penaltyregime>33.3, 66.7, ...</penaltyregime>", 33)]
    [InlineData("<penaltyregime>0</penaltyregime>", 0)]
    [InlineData("<penaltyregime></penaltyregime><penalty>0.2500000</penalty>", 25)]
    [InlineData("", 10)]
    public void Import_ReadsThePenalty(string extra, int percent)
    {
        Assert.Equal(percent, Assert.Single(MoodleXmlParser.Parse(CodeRunnerXml(extra)).Questions).Code!.CheckPenaltyPercent);
    }

    [Theory]
    [InlineData(10, "10, 20, ...")]
    [InlineData(60, "60, 100, ...")]
    [InlineData(0, "0")]
    public void Export_WritesThePenaltyRegime_AndImportReadsItBack(int percent, string regime)
    {
        var question = new Question
        {
            Text = "Сбор", Type = QuestionType.Code, CodeLanguage = "python3", CheckPenaltyPercent = percent,
            TestCases = { new CodeTestCase { Stdin = "1", Expected = "1" } },
        };

        var xml = MoodleXmlWriter.Write(new[] { question });

        Assert.Contains($"<penaltyregime>{regime}</penaltyregime>", xml);
        Assert.Equal(percent, Assert.Single(MoodleXmlParser.Parse(xml).Questions).Code!.CheckPenaltyPercent);
    }
}
