using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>
/// The server checks the exam's time limit when an attempt is submitted: a late submission is accepted
/// but marked with how late it was.
/// </summary>
public class ExamTimingTests
{
    private const string Owner = "teacher-1";

    private static readonly DateTime Start = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);

    private static QuizAttempt Attempt(int? quizLimit, int? classExamLimit = null, bool classExam = false)
        => new()
        {
            StartedOn = Start,
            Quiz = new Quiz { Title = "Изпит", TimeLimitMinutes = quizLimit },
            ExamAssignment = classExam ? new ExamAssignment { TimeLimitMinutes = classExamLimit } : null
        };

    // ---------- LateBySeconds ----------

    [Fact]
    public void LateBySeconds_WithinTheLimit_IsNull()
        => Assert.Null(ExamTiming.LateBySeconds(Attempt(30), Start.AddMinutes(29)));

    [Fact]
    public void LateBySeconds_WithinTheGracePeriod_IsNull()
        => Assert.Null(ExamTiming.LateBySeconds(Attempt(30), Start.AddMinutes(30).AddSeconds(ExamTiming.GraceSeconds)));

    [Fact]
    public void LateBySeconds_AfterTheGracePeriod_CountsFromTheLimit()
        => Assert.Equal(ExamTiming.GraceSeconds + 1,
            ExamTiming.LateBySeconds(Attempt(30), Start.AddMinutes(30).AddSeconds(ExamTiming.GraceSeconds + 1)));

    [Fact]
    public void LateBySeconds_WithoutATimeLimit_IsNull()
        => Assert.Null(ExamTiming.LateBySeconds(Attempt(null), Start.AddHours(5)));

    [Fact]
    public void LateBySeconds_UsesTheClassExamLimit_NotTheQuizs()
    {
        // The quiz itself allows 60 minutes, the class exam only 10
        var attempt = Attempt(60, classExamLimit: 10, classExam: true);

        Assert.Equal(5 * 60, ExamTiming.LateBySeconds(attempt, Start.AddMinutes(15)));
    }

    [Fact]
    public void LateBySeconds_AClassExamWithoutALimit_IsNull_EvenIfTheQuizHasOne()
        => Assert.Null(ExamTiming.LateBySeconds(Attempt(10, classExamLimit: null, classExam: true), Start.AddMinutes(30)));

    [Theory]
    [InlineData(45, "закъснял с 45 сек.")]
    [InlineData(60, "закъснял с 1 мин.")]
    [InlineData(61, "закъснял с 2 мин.")]
    [InlineData(180, "закъснял с 3 мин.")]
    public void LateLabel_ShowsSecondsOrRoundedUpMinutes(int seconds, string expected)
        => Assert.Equal(expected, ExamTiming.LateLabel(seconds));

    // ---------- SubmitExamAsync ----------

    private static async Task<(QuizService Service, QuizAttempt Attempt)> StartedAsync(Data.ApplicationDbContext context, int minutes)
    {
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, minutes, null), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0), Owner, false);
        await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        var attempt = (await service.StartExamAsync(quiz.Code!, "Иван", "1")).Value!;
        return (service, attempt);
    }

    private static async Task MoveStartAsync(Data.ApplicationDbContext context, QuizAttempt attempt, TimeSpan ago)
    {
        var tracked = await context.QuizAttempts.SingleAsync(a => a.Id == attempt.Id);
        tracked.StartedOn = DateTime.UtcNow - ago;
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task SubmitExamAsync_OnTime_IsNotLate()
    {
        using var context = TestDbContextFactory.Create();
        var (service, attempt) = await StartedAsync(context, 30);

        await service.SubmitExamAsync(attempt.Token, new Dictionary<int, SubmittedAnswer>());

        context.ChangeTracker.Clear();
        var saved = await context.QuizAttempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.NotNull(saved.SubmittedOn);
        Assert.Null(saved.LateBySeconds);
    }

    [Fact]
    public async Task SubmitExamAsync_AfterTheLimit_IsAcceptedAndMarkedLate()
    {
        using var context = TestDbContextFactory.Create();
        var (service, attempt) = await StartedAsync(context, 10);
        await MoveStartAsync(context, attempt, TimeSpan.FromMinutes(15));
        var question = (await context.Questions.SingleAsync(q => q.QuizId == attempt.QuizId));
        var correct = await context.AnswerOptions.Where(o => o.QuestionId == question.Id).OrderBy(o => o.Order).Select(o => o.Id).FirstAsync();

        var result = await service.SubmitExamAsync(attempt.Token,
            new Dictionary<int, SubmittedAnswer> { [question.Id] = SubmittedAnswer.Choice(correct) });

        Assert.True(result.Succeeded);
        context.ChangeTracker.Clear();
        var saved = await context.QuizAttempts.SingleAsync(a => a.Id == attempt.Id);
        Assert.Equal(1m, saved.Score);
        Assert.InRange(saved.LateBySeconds!.Value, 5 * 60 - 5, 5 * 60 + 5);
    }

    [Fact]
    public async Task SubmitExamAsync_Resubmitting_DoesNotChangeTheLateness()
    {
        using var context = TestDbContextFactory.Create();
        var (service, attempt) = await StartedAsync(context, 30);
        await service.SubmitExamAsync(attempt.Token, new Dictionary<int, SubmittedAnswer>());

        // A second submit long after the limit (e.g. the timer firing again) is ignored
        await MoveStartAsync(context, attempt, TimeSpan.FromHours(2));
        await service.SubmitExamAsync(attempt.Token, new Dictionary<int, SubmittedAnswer>());

        context.ChangeTracker.Clear();
        Assert.Null((await context.QuizAttempts.SingleAsync(a => a.Id == attempt.Id)).LateBySeconds);
    }
}
