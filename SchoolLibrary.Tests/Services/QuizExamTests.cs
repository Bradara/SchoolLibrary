using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class QuizExamTests
{
    private const string Owner = "teacher-1";

    private static async Task<Quiz> PublishedExamAsync(QuizService service, int minutes = 30, int questions = 2)
    {
        var quiz = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, minutes, null), Owner)).Value!;
        for (var i = 0; i < questions; i++)
        {
            await service.AddQuestionAsync(quiz.Id, new QuestionInput("Въпрос " + i, new[] { "верен", "грешен" }, 0), Owner, false);
        }

        await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        return (await service.GetByIdAsync(quiz.Id))!;
    }

    // ---------- StartExamAsync ----------

    [Fact]
    public async Task StartExamAsync_CreatesAnAttemptWithATokenAndTheQuestionCount()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var exam = await PublishedExamAsync(service, questions: 3);

        var result = await service.StartExamAsync(exam.Code!, "Иван Петров", "5");

        Assert.True(result.Succeeded);
        var attempt = result.Value!;
        Assert.NotEqual(Guid.Empty, attempt.Token);
        Assert.Equal("Иван Петров", attempt.StudentName);
        Assert.Equal("5", attempt.StudentNumber);
        Assert.Equal(3, attempt.MaxScore);
        Assert.Null(attempt.SubmittedOn);
        Assert.NotEqual(default, attempt.StartedOn);
    }

    [Fact]
    public async Task StartExamAsync_UnknownCode_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);

        var result = await service.StartExamAsync("ZZZZZZ", "Иван", "1");

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task StartExamAsync_UnpublishedExam_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var exam = (await service.CreateAsync(new QuizInput("Чернова", null, QuizMode.Exam, 30, null), Owner)).Value!;
        await service.AddQuestionAsync(exam.Id, new QuestionInput("В", new[] { "а", "б" }, 0), Owner, false);
        // not published

        var result = await service.StartExamAsync(exam.Code!, "Иван", "1");

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task StartExamAsync_IgnoresCodeCaseAndWhitespace()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var exam = await PublishedExamAsync(service);

        var result = await service.StartExamAsync("  " + exam.Code!.ToLower() + "  ", "Иван", "1");

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StartExamAsync_WithoutName_FailsAsInvalid(string name)
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var exam = await PublishedExamAsync(service);

        var result = await service.StartExamAsync(exam.Code!, name, "1");

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task StartExamAsync_ResumesAnUnfinishedAttempt_InsteadOfCreatingASecond()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var exam = await PublishedExamAsync(service);

        var first = (await service.StartExamAsync(exam.Code!, "Мария", null)).Value!;
        var second = (await service.StartExamAsync(exam.Code!, "мария", null)).Value!;

        Assert.Equal(first.Token, second.Token);
        Assert.Equal(1, await context.QuizAttempts.CountAsync());
    }

    [Fact]
    public async Task StartExamAsync_AfterSubmitting_FailsAsConflict()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var exam = await PublishedExamAsync(service);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        await service.SubmitExamAsync(attempt.Token, new Dictionary<int, int?>());

        var again = await service.StartExamAsync(exam.Code!, "Иван", "5");

        Assert.Equal(ServiceError.Conflict, again.Error);
    }

    // ---------- GetExamAttemptAsync ----------

    [Fact]
    public async Task GetExamAttemptAsync_LoadsTheQuizWithItsQuestions()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var exam = await PublishedExamAsync(service, questions: 2);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;

        var loaded = await service.GetExamAttemptAsync(attempt.Token);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Quiz.Questions.Count);
    }

    [Fact]
    public async Task GetExamAttemptAsync_UnknownToken_ReturnsNull()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);

        Assert.Null(await service.GetExamAttemptAsync(Guid.NewGuid()));
    }

    // ---------- SubmitExamAsync ----------

    private static Dictionary<int, int?> CorrectAnswers(Quiz quiz)
        => quiz.Questions.ToDictionary(q => q.Id, q => (int?)q.Options.Single(o => o.IsCorrect).Id);

    [Fact]
    public async Task SubmitExamAsync_GradesAndStoresTheAttempt()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var exam = await PublishedExamAsync(service, questions: 2);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var loaded = (await service.GetExamAttemptAsync(attempt.Token))!;

        var result = await service.SubmitExamAsync(attempt.Token, CorrectAnswers(loaded.Quiz));

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Score);

        var stored = await context.QuizAttempts.FirstAsync(a => a.Token == attempt.Token);
        Assert.NotNull(stored.SubmittedOn);
        Assert.Equal(2, stored.Score);
        Assert.Equal(2, await context.AttemptAnswers.CountAsync(a => a.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task SubmitExamAsync_Resubmitting_ReturnsTheStoredResultUnchanged()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var exam = await PublishedExamAsync(service, questions: 2);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var loaded = (await service.GetExamAttemptAsync(attempt.Token))!;

        await service.SubmitExamAsync(attempt.Token, CorrectAnswers(loaded.Quiz));
        var submittedOn = (await context.QuizAttempts.FirstAsync(a => a.Token == attempt.Token)).SubmittedOn;

        // A second submit (e.g. the timer firing) with no answers must not lower the score or add rows
        var second = await service.SubmitExamAsync(attempt.Token, new Dictionary<int, int?>());

        Assert.True(second.Succeeded);
        Assert.Equal(2, second.Value!.Score);
        Assert.Equal(2, await context.AttemptAnswers.CountAsync(a => a.AttemptId == attempt.Id));
        Assert.Equal(submittedOn, (await context.QuizAttempts.FirstAsync(a => a.Token == attempt.Token)).SubmittedOn);
    }

    [Fact]
    public async Task SubmitExamAsync_UnknownToken_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);

        var result = await service.SubmitExamAsync(Guid.NewGuid(), new Dictionary<int, int?>());

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task SubmitExamAsync_CountsOnlyTheCorrectAnswers()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var exam = await PublishedExamAsync(service, questions: 2);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var loaded = (await service.GetExamAttemptAsync(attempt.Token))!;

        // Answer the first question correctly, the second wrongly
        var questions = loaded.Quiz.Questions.ToList();
        var answers = new Dictionary<int, int?>
        {
            [questions[0].Id] = questions[0].Options.Single(o => o.IsCorrect).Id,
            [questions[1].Id] = questions[1].Options.Single(o => !o.IsCorrect).Id
        };

        var result = await service.SubmitExamAsync(attempt.Token, answers);

        Assert.Equal(1, result.Value!.Score);
        Assert.Equal(2, result.Value!.MaxScore);
    }

    // ---------- GetExamResultAsync ----------

    [Fact]
    public async Task GetExamResultAsync_AfterSubmitting_ReturnsTheResult()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var exam = await PublishedExamAsync(service, questions: 2);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var loaded = (await service.GetExamAttemptAsync(attempt.Token))!;
        await service.SubmitExamAsync(attempt.Token, CorrectAnswers(loaded.Quiz));

        var result = await service.GetExamResultAsync(attempt.Token);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Score);
    }

    [Fact]
    public async Task GetExamResultAsync_BeforeSubmitting_FailsAsInvalid()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var exam = await PublishedExamAsync(service);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;

        var result = await service.GetExamResultAsync(attempt.Token);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task GetExamResultAsync_UnknownToken_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);

        var result = await service.GetExamResultAsync(Guid.NewGuid());

        Assert.Equal(ServiceError.NotFound, result.Error);
    }
}
