using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolLibrary.BackgroundJobs;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.CodeRunning;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>The programs of submitted exams are checked in the background, not while the student waits.</summary>
public class CodeGradingQueueTests
{
    private const string Owner = "teacher-1";
    private const string Program = "print(sum(map(int, input().split())))";

    private static Mock<ICodeRunner> Runner(CodeRunResult result)
    {
        var runner = new Mock<ICodeRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return runner;
    }

    private static CodeRunResult Outputs(params string[] outputs)
        => new(CodeRunStatus.Completed, outputs.Select(o => new CodeTestOutput(o, "", 0, false)).ToList());

    // An exam with a single-choice question (1 point) and a code question (4 points), started by one student
    private static async Task<(QuizService Service, QuizAttempt Attempt, Question Choice, Question Code)> ExamAsync(
        Data.ApplicationDbContext context, Mock<ICodeRunner> runner, CodeGradingSignal signal)
    {
        var service = new QuizService(context, new QuizItemService(context), new CodeGrader(runner.Object), signal);
        var quiz = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null), Owner)).Value!;
        var choice = (await service.AddQuestionAsync(quiz.Id, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0), Owner, false)).Value!;
        var code = (await service.AddQuestionAsync(quiz.Id, QuestionInput.CodeQuestion("Сбор", CodeLanguages.Python,
            new[] { new CodeTestInput("2 3", "5"), new CodeTestInput("-10 4", "-6") }, points: 4), Owner, false)).Value!;
        await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        quiz = (await service.GetByIdAsync(quiz.Id))!;
        var attempt = (await service.StartExamAsync(quiz.Code!, "Иван", "5")).Value!;
        return (service, attempt, choice, code);
    }

    private static IReadOnlyDictionary<int, SubmittedAnswer> Answers(Question choice, Question code, string? program = Program)
        => SubmittedAnswer.FromForm(new Dictionary<int, int?> { [choice.Id] = choice.Options.First(o => o.IsCorrect).Id },
            null, null, null, new Dictionary<int, string?> { [code.Id] = program });

    // ---------- Submitting ----------

    [Fact]
    public async Task Submit_LeavesTheProgramWaiting_CountsTheRest_AndWakesTheChecker()
    {
        using var context = TestDbContextFactory.Create();
        var runner = Runner(Outputs("5", "-6"));
        var signal = new CodeGradingSignal();
        var (service, attempt, choice, code) = await ExamAsync(context, runner, signal);

        var result = (await service.SubmitExamAsync(attempt.Token, Answers(choice, code))).Value!;

        Assert.True(result.HasPendingCode);
        Assert.Equal(1m, result.Score);
        Assert.Equal(5m, result.MaxScore);
        runner.VerifyNoOtherCalls();
        Assert.True(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));

        context.ChangeTracker.Clear();
        var stored = await context.AttemptAnswers.SingleAsync(a => a.QuestionId == code.Id);
        Assert.True(stored.CodeCheckPending);
        Assert.Equal(Program, stored.ResponseText);
        Assert.Null(stored.CodeResults);

        var shown = (await service.GetExamResultAsync(attempt.Token)).Value!;
        Assert.True(shown.HasPendingCode);
        Assert.True(shown.Questions.Single(q => q.IsCode).CodePending);
    }

    [Fact]
    public async Task Submit_BlankProgram_IsNotLeftWaiting()
    {
        using var context = TestDbContextFactory.Create();
        var signal = new CodeGradingSignal();
        var (service, attempt, choice, code) = await ExamAsync(context, Runner(Outputs()), signal);

        var result = (await service.SubmitExamAsync(attempt.Token, Answers(choice, code, " "))).Value!;

        Assert.False(result.HasPendingCode);
        Assert.False(await context.AttemptAnswers.AnyAsync(a => a.CodeCheckPending));
        Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }

    // ---------- Checking in the background ----------

    [Fact]
    public async Task GradePending_StoresTheResult_AndAddsThePointsToTheAttempt()
    {
        using var context = TestDbContextFactory.Create();
        var runner = Runner(Outputs("5", "-6"));
        var (service, attempt, choice, code) = await ExamAsync(context, runner, new CodeGradingSignal());
        await service.SubmitExamAsync(attempt.Token, Answers(choice, code));
        context.ChangeTracker.Clear();

        var run = await new CodeGradingService(context, new CodeGrader(runner.Object)).GradePendingAsync(4);

        Assert.Equal(new CodeGradingRun(1, 0, false), run);
        context.ChangeTracker.Clear();
        var stored = await context.AttemptAnswers.SingleAsync(a => a.QuestionId == code.Id);
        Assert.False(stored.CodeCheckPending);
        Assert.Equal(4m, stored.AwardedPoints);
        Assert.True(stored.IsCorrect);
        Assert.Equal(CodeGradeStatus.Graded, StoredCodeResults.FromJson(stored.CodeResults)!.Status);
        Assert.Equal(5m, (await context.QuizAttempts.SingleAsync()).Score);

        var shown = (await service.GetExamResultAsync(attempt.Token)).Value!;
        Assert.False(shown.HasPendingCode);
        Assert.Equal(5m, shown.Score);
    }

    [Fact]
    public async Task GradePending_ServerUnavailable_KeepsItWaiting()
    {
        using var context = TestDbContextFactory.Create();
        var (service, attempt, choice, code) = await ExamAsync(context, Runner(Outputs()), new CodeGradingSignal());
        await service.SubmitExamAsync(attempt.Token, Answers(choice, code));
        context.ChangeTracker.Clear();
        var down = Runner(CodeRunResult.Failed(CodeRunStatus.Unavailable, "Connection refused"));

        var run = await new CodeGradingService(context, new CodeGrader(down.Object)).GradePendingAsync(4);

        Assert.Equal(new CodeGradingRun(0, 1, true), run);
        context.ChangeTracker.Clear();
        Assert.True((await context.AttemptAnswers.SingleAsync(a => a.QuestionId == code.Id)).CodeCheckPending);
        Assert.Equal(1m, (await context.QuizAttempts.SingleAsync()).Score);
    }

    [Fact]
    public async Task GradePending_CompileError_IsFinal_WithNoPoints()
    {
        using var context = TestDbContextFactory.Create();
        var (service, attempt, choice, code) = await ExamAsync(context, Runner(Outputs()), new CodeGradingSignal());
        await service.SubmitExamAsync(attempt.Token, Answers(choice, code));
        context.ChangeTracker.Clear();
        var broken = Runner(CodeRunResult.Failed(CodeRunStatus.CompileError, "SyntaxError"));

        var run = await new CodeGradingService(context, new CodeGrader(broken.Object)).GradePendingAsync(4);

        Assert.Equal(new CodeGradingRun(1, 0, false), run);
        var stored = await context.AttemptAnswers.SingleAsync(a => a.QuestionId == code.Id);
        Assert.Equal("SyntaxError", StoredCodeResults.FromJson(stored.CodeResults)!.Message);
    }

    [Fact]
    public async Task GradePending_TakesOnlyTheBatch_OldestFirst()
    {
        using var context = TestDbContextFactory.Create();
        var question = new Question { Text = "Сбор", Type = QuestionType.Code, Points = 2, CodeLanguage = CodeLanguages.Python,
            TestCases = { new CodeTestCase { Stdin = "1", Expected = "1" } } };
        var quiz = new Quiz { Title = "Изпит", OwnerId = Owner, Questions = { question } };
        context.Quizzes.Add(quiz);
        for (var i = 0; i < 3; i++)
        {
            var attempt = new QuizAttempt { Quiz = quiz, Token = Guid.NewGuid(), StudentName = $"Ученик {i}", StartedOn = DateTime.UtcNow, SubmittedOn = DateTime.UtcNow };
            attempt.Answers.Add(new AttemptAnswer { Question = question, ResponseText = $"print({i})", CodeCheckPending = true });
            context.QuizAttempts.Add(attempt);
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var runner = Runner(Outputs("1"));

        var run = await new CodeGradingService(context, new CodeGrader(runner.Object)).GradePendingAsync(2);

        Assert.Equal(new CodeGradingRun(2, 1, false), run);
        runner.Verify(r => r.RunAsync(It.IsAny<string>(), "print(0)", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()));
        runner.Verify(r => r.RunAsync(It.IsAny<string>(), "print(1)", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()));
        Assert.Equal("print(2)", (await context.AttemptAnswers.SingleAsync(a => a.CodeCheckPending)).ResponseText);
    }

    [Fact]
    public async Task GradePending_QuestionNoLongerCode_IsNoLongerWaiting()
    {
        using var context = TestDbContextFactory.Create();
        var question = new Question { Text = "2 + 2", Type = QuestionType.SingleChoice, Points = 1 };
        var attempt = new QuizAttempt { Quiz = new Quiz { Title = "Изпит", OwnerId = Owner }, Token = Guid.NewGuid(), StudentName = "Ученик", StartedOn = DateTime.UtcNow };
        attempt.Answers.Add(new AttemptAnswer { Question = question, CodeCheckPending = true });
        context.QuizAttempts.Add(attempt);
        await context.SaveChangesAsync();
        var runner = Runner(Outputs());

        var run = await new CodeGradingService(context, new CodeGrader(runner.Object)).GradePendingAsync(4);

        Assert.Equal(new CodeGradingRun(0, 0, false), run);
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GradePending_WithoutAServer_ChecksNothing()
    {
        using var context = TestDbContextFactory.Create();
        context.AttemptAnswers.Add(new AttemptAnswer
        {
            Attempt = new QuizAttempt { Quiz = new Quiz { Title = "И" }, Token = Guid.NewGuid(), StudentName = "У", StartedOn = DateTime.UtcNow },
            Question = new Question { Text = "К", Type = QuestionType.Code }, CodeCheckPending = true
        });
        await context.SaveChangesAsync();

        var run = await new CodeGradingService(context).GradePendingAsync(4);

        Assert.Equal(new CodeGradingRun(0, 1, true), run);
    }

    [Fact]
    public async Task GradePending_NothingWaiting_DoesNothing()
    {
        using var context = TestDbContextFactory.Create();
        var runner = Runner(Outputs());

        Assert.Equal(new CodeGradingRun(0, 0, false), await new CodeGradingService(context, new CodeGrader(runner.Object)).GradePendingAsync(4));
        runner.VerifyNoOtherCalls();
    }

    // ---------- Signal ----------

    [Fact]
    public async Task Signal_MergesWakeUps_AndTimesOut()
    {
        var signal = new CodeGradingSignal();

        Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
        signal.Notify();
        signal.Notify();
        Assert.True(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.False(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }

    // ---------- Worker ----------

    private static CodeGradingWorker Worker(Mock<ICodeGradingService> grading)
    {
        var provider = new ServiceCollection().AddScoped(_ => grading.Object).BuildServiceProvider();
        return new CodeGradingWorker(provider.GetRequiredService<IServiceScopeFactory>(), new CodeGradingSignal(),
            NullLogger<CodeGradingWorker>.Instance);
    }

    [Theory]
    [InlineData(4, 3, false, 0)]   // more waiting: next round at once
    [InlineData(1, 0, false, 30)]  // all done: sleep until woken
    [InlineData(0, 2, true, 60)]   // server down: try again later
    public async Task Worker_Round_SaysHowLongToWait(int graded, int remaining, bool unavailable, int seconds)
    {
        var grading = new Mock<ICodeGradingService>();
        grading.Setup(g => g.GradePendingAsync(CodeGradingWorker.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodeGradingRun(graded, remaining, unavailable));

        Assert.Equal(TimeSpan.FromSeconds(seconds), await Worker(grading).RunRoundAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Worker_Round_Failure_WaitsAndTriesAgainLater()
    {
        var grading = new Mock<ICodeGradingService>();
        grading.Setup(g => g.GradePendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        Assert.Equal(CodeGradingWorker.RetryWait, await Worker(grading).RunRoundAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Worker_ChecksWhenStarted_AndStopsCleanly()
    {
        var grading = new Mock<ICodeGradingService>();
        var called = new TaskCompletionSource();
        grading.Setup(g => g.GradePendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => called.TrySetResult())
            .ReturnsAsync(new CodeGradingRun(0, 0, false));
        var worker = Worker(grading);

        await worker.StartAsync(CancellationToken.None);
        await called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
    }
}
