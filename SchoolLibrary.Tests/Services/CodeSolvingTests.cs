using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Moq;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.CodeRunning;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>Solving code questions: the program is run when a practice or an exam is submitted.</summary>
public class CodeSolvingTests
{
    private const string Owner = "teacher-1";
    private const string Program = "a, b = map(int, input().split())\nprint(a + b)";

    private static readonly CodeTestInput[] Tests =
    {
        new("2 3", "5", IsExample: true),
        new("-10 4", "-6", IsHidden: true),
    };

    private static Mock<ICodeRunner> Runner(params string[] outputs)
    {
        var runner = new Mock<ICodeRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodeRunResult(CodeRunStatus.Completed, outputs.Select(o => new CodeTestOutput(o, "", 0, false)).ToList()));
        return runner;
    }

    private static QuizService Service(Data.ApplicationDbContext context, Mock<ICodeRunner>? runner)
        => new(context, new QuizItemService(context), runner == null ? null : new CodeGrader(runner.Object));

    // A published quiz with a single-choice question and a code question worth 4 points
    private static async Task<(Quiz Quiz, Question Choice, Question Code)> QuizAsync(QuizService service, QuizMode mode)
    {
        var quiz = (await service.CreateAsync(new QuizInput("Програмиране", null, mode, mode == QuizMode.Exam ? 30 : null, null), Owner)).Value!;
        var choice = (await service.AddQuestionAsync(quiz.Id, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0), Owner, false)).Value!;
        var code = (await service.AddQuestionAsync(quiz.Id,
            QuestionInput.CodeQuestion("Сбор на две числа", CodeLanguages.Python, Tests, points: 4), Owner, false)).Value!;
        await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        return ((await service.GetByIdAsync(quiz.Id))!, choice, code);
    }

    private static IReadOnlyDictionary<int, SubmittedAnswer> Answers(Question choice, Question code, string? program = Program)
        => SubmittedAnswer.FromForm(
            new Dictionary<int, int?> { [choice.Id] = choice.Options.First(o => o.IsCorrect).Id }, null, null, null,
            new Dictionary<int, string?> { [code.Id] = program });

    // ---------- Practice ----------

    [Fact]
    public async Task Practice_RunsTheProgram_AndAddsItsPoints()
    {
        using var context = TestDbContextFactory.Create();
        var runner = Runner("5\n", "-6\n");
        var service = Service(context, runner);
        var (quiz, choice, code) = await QuizAsync(service, QuizMode.Practice);

        var result = (await service.GradePracticeAsync(quiz.Id, new[] { choice.Id, code.Id }, Answers(choice, code))).Value!;

        Assert.Equal(5m, result.Score);
        Assert.Equal(5m, result.MaxScore);
        var codeResult = result.Questions.Single(q => q.IsCode);
        Assert.True(codeResult.IsCorrect);
        Assert.Equal(Program, codeResult.TypedText);
        Assert.Equal(CodeGradeStatus.Graded, codeResult.CodeResults!.Status);
        Assert.Equal(new[] { ("2 3", false), ("-10 4", true) }, codeResult.CodeResults.Tests.Select(t => (t.Stdin, t.IsHidden)));
        runner.Verify(r => r.RunAsync("python3", Program,
            It.Is<IReadOnlyList<string>>(s => s.SequenceEqual(new[] { "2 3", "-10 4" })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Practice_FailedTest_AllOrNothing_NoPointsForTheCode()
    {
        using var context = TestDbContextFactory.Create();
        var service = Service(context, Runner("5", "6"));
        var (quiz, choice, code) = await QuizAsync(service, QuizMode.Practice);

        var result = (await service.GradePracticeAsync(quiz.Id, new[] { choice.Id, code.Id }, Answers(choice, code))).Value!;

        Assert.Equal(1m, result.Score);
        Assert.False(result.Questions.Single(q => q.IsCode).IsCorrect);
    }

    [Fact]
    public async Task Practice_BlankProgram_ServerNotAsked()
    {
        using var context = TestDbContextFactory.Create();
        var runner = Runner();
        var service = Service(context, runner);
        var (quiz, choice, code) = await QuizAsync(service, QuizMode.Practice);

        var result = (await service.GradePracticeAsync(quiz.Id, new[] { choice.Id, code.Id }, Answers(choice, code, "  \n"))).Value!;

        var codeResult = result.Questions.Single(q => q.IsCode);
        Assert.Null(codeResult.TypedText);
        Assert.Null(codeResult.CodeResults);
        Assert.Equal(0m, codeResult.AwardedPoints);
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Practice_WithoutASandboxServer_IsNotChecked()
    {
        using var context = TestDbContextFactory.Create();
        var service = Service(context, runner: null);
        var (quiz, choice, code) = await QuizAsync(service, QuizMode.Practice);

        var result = (await service.GradePracticeAsync(quiz.Id, new[] { choice.Id, code.Id }, Answers(choice, code))).Value!;

        var codeResult = result.Questions.Single(q => q.IsCode);
        Assert.Equal(CodeGradeStatus.NotChecked, codeResult.CodeResults!.Status);
        Assert.Equal("Проверката на код не е настроена.", codeResult.CodeResults.Message);
        Assert.Equal(Program, codeResult.TypedText);
        Assert.Equal(1m, result.Score);
    }

    [Fact]
    public async Task PracticeSet_LoadsTheTestsForTheExamples()
    {
        using var context = TestDbContextFactory.Create();
        var service = Service(context, Runner());
        var (quiz, _, _) = await QuizAsync(service, QuizMode.Practice);

        var set = (await service.GetPracticeSetAsync(quiz.Id)).Value!;

        Assert.Equal(2, set.Questions.Single(q => q.Type == QuestionType.Code).TestCases.Count);
    }

    // ---------- Exam ----------

    [Fact]
    public async Task Exam_StoresTheProgramAndItsResults_AndTheResultIsNotRunAgain()
    {
        using var context = TestDbContextFactory.Create();
        var runner = Runner("5", "0");
        var service = Service(context, runner);
        var (quiz, choice, code) = await QuizAsync(service, QuizMode.Exam);
        var attempt = (await service.StartExamAsync(quiz.Code!, "Иван", "5")).Value!;

        var attemptView = (await service.GetExamAttemptAsync(attempt.Token))!;
        Assert.Equal(2, attemptView.Questions.Single(q => q.Question.Type == QuestionType.Code).Question.TestCases.Count);

        var submitted = await service.SubmitExamAsync(attempt.Token, Answers(choice, code));
        Assert.True(submitted.Succeeded);

        context.ChangeTracker.Clear();
        var stored = await context.AttemptAnswers.SingleAsync(a => a.QuestionId == code.Id);
        Assert.Equal(Program, stored.ResponseText);
        Assert.Equal(0m, stored.AwardedPoints); // all or nothing, the hidden test failed
        var results = StoredCodeResults.FromJson(stored.CodeResults)!;
        Assert.Equal(new[] { true, false }, results.Tests.Select(t => t.Passed));
        Assert.Equal("0", results.Tests[1].Output);

        var result = (await service.GetExamResultAsync(attempt.Token)).Value!;
        var codeResult = result.Questions.Single(q => q.IsCode);
        Assert.Equal(Program, codeResult.TypedText);
        Assert.Equal(new[] { true, false }, codeResult.CodeResults!.Tests.Select(t => t.Passed));
        Assert.Equal(1m, result.Score);
        runner.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Exam_PartialCredit_IsStored()
    {
        using var context = TestDbContextFactory.Create();
        var service = Service(context, Runner("5", "0"));
        var quiz = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null), Owner)).Value!;
        var code = (await service.AddQuestionAsync(quiz.Id,
            QuestionInput.CodeQuestion("Сбор", CodeLanguages.CSharp, Tests, allOrNothing: false, points: 4), Owner, false)).Value!;
        await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        quiz = (await service.GetByIdAsync(quiz.Id))!;
        var attempt = (await service.StartExamAsync(quiz.Code!, "Мария", "7")).Value!;

        await service.SubmitExamAsync(attempt.Token, SubmittedAnswer.FromForm(null, null, null, null,
            new Dictionary<int, string?> { [code.Id] = "class P {}" }));

        context.ChangeTracker.Clear();
        var saved = await context.QuizAttempts.SingleAsync();
        Assert.Equal(2m, saved.Score);
        Assert.Equal(4m, saved.MaxScore);
    }

    // ---------- Grading and form ----------

    [Fact]
    public void Grading_CodeQuestionWithoutAnOutcome_EarnsNothing()
    {
        var quiz = new Quiz { Title = "Т" };
        var question = new Question { Id = 3, Type = QuestionType.Code, Points = 2 };

        var result = QuizGrading.Grade(quiz, new[] { question },
            new Dictionary<int, SubmittedAnswer> { [3] = SubmittedAnswer.Program("print(1)") });

        Assert.Equal(0m, result.Score);
        Assert.Equal(2m, result.MaxScore);
        Assert.Equal("print(1)", result.Questions[0].TypedText);
        Assert.Null(result.Questions[0].CodeResults);
    }

    [Fact]
    public void Program_KeepsLeadingWhiteSpace_TrimsTheEnd_AndCutsTheLength()
    {
        Assert.Equal("    x = 1", SubmittedAnswer.Program("    x = 1  \n\n").Text);
        Assert.Same(SubmittedAnswer.Empty, SubmittedAnswer.Program(" \n\t"));
        Assert.Same(SubmittedAnswer.Empty, SubmittedAnswer.Program(null));
        Assert.Equal(CodeGrader.MaxCodeLength, SubmittedAnswer.Program(new string('x', CodeGrader.MaxCodeLength + 5)).Text!.Length);
    }

    [Fact]
    public void FromForm_ReadsPrograms()
    {
        var answers = SubmittedAnswer.FromForm(null, null, null, null, new Dictionary<int, string?> { [4] = "print(1)\n", [5] = "" });

        Assert.Equal("print(1)", answers[4].Text);
        Assert.Same(SubmittedAnswer.Empty, answers[5]);
    }

    // ---------- The line in front of the server ----------

    [Fact]
    public async Task Gate_LetsInOnlyTheAllowedNumber_AndTheNextWaitsForAFreeSlot()
    {
        var gate = new CodeRunGate(new CodeRunnerOptions { MaxConcurrentRuns = 1, QueueTimeoutSeconds = 1 });

        var first = await gate.EnterAsync(CancellationToken.None);
        Assert.NotNull(first);
        Assert.Null(await gate.EnterAsync(CancellationToken.None)); // gives up after the queue timeout

        first!.Dispose();
        first.Dispose(); // freeing twice does not add a slot
        using var second = await gate.EnterAsync(CancellationToken.None);
        Assert.NotNull(second);
        Assert.Null(await gate.EnterAsync(CancellationToken.None));
    }

    private sealed class OkJobe : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"outcome\":15,\"stdout\":\"{\\\"tests\\\":[]}\",\"stderr\":\"\"}", Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task Runner_NoFreeSlot_IsUnavailable_WithoutCallingTheServer()
    {
        var options = new CodeRunnerOptions { BaseUrl = "http://jobe", MaxConcurrentRuns = 1, QueueTimeoutSeconds = 1 };
        var gate = new CodeRunGate(options);
        var jobe = new OkJobe();
        var runner = new JobeCodeRunner(new HttpClient(jobe) { BaseAddress = new Uri("http://jobe/") }, options, gate);

        using (await gate.EnterAsync(CancellationToken.None))
        {
            var busy = await runner.RunAsync(CodeLanguages.Python, "print(1)", ["1"]);
            Assert.Equal(CodeRunStatus.Unavailable, busy.Status);
            Assert.Equal(0, jobe.Requests);
        }

        var free = await runner.RunAsync(CodeLanguages.Python, "print(1)", ["1"]);
        Assert.Equal(CodeRunStatus.Completed, free.Status);
        Assert.Equal(1, jobe.Requests);
        Assert.NotNull(await gate.EnterAsync(CancellationToken.None)); // the run gave its slot back
    }
}
