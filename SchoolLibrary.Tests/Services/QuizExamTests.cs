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
        var service = new QuizService(context, new QuizItemService(context));
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
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.StartExamAsync("ZZZZZZ", "Иван", "1");

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task StartExamAsync_UnpublishedExam_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
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
        var service = new QuizService(context, new QuizItemService(context));
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
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamAsync(service);

        var result = await service.StartExamAsync(exam.Code!, name, "1");

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task StartExamAsync_ResumesAnUnfinishedAttempt_InsteadOfCreatingASecond()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
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
        var service = new QuizService(context, new QuizItemService(context));
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
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamAsync(service, questions: 2);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;

        var loaded = await service.GetExamAttemptAsync(attempt.Token);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Questions.Count);
    }

    [Fact]
    public async Task GetExamAttemptAsync_UnknownToken_ReturnsNull()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        Assert.Null(await service.GetExamAttemptAsync(Guid.NewGuid()));
    }

    // ---------- SubmitExamAsync ----------

    private static Dictionary<int, int?> CorrectAnswers(QuizAttempt attempt)
        => attempt.Questions.Select(aq => aq.Question)
            .ToDictionary(q => q.Id, q => (int?)q.Options.Single(o => o.IsCorrect).Id);

    [Fact]
    public async Task SubmitExamAsync_GradesAndStoresTheAttempt()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamAsync(service, questions: 2);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var loaded = (await service.GetExamAttemptAsync(attempt.Token))!;

        var result = await service.SubmitExamAsync(attempt.Token, CorrectAnswers(loaded));

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
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamAsync(service, questions: 2);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var loaded = (await service.GetExamAttemptAsync(attempt.Token))!;

        await service.SubmitExamAsync(attempt.Token, CorrectAnswers(loaded));
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
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.SubmitExamAsync(Guid.NewGuid(), new Dictionary<int, int?>());

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task SubmitExamAsync_CountsOnlyTheCorrectAnswers()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamAsync(service, questions: 2);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var loaded = (await service.GetExamAttemptAsync(attempt.Token))!;

        // Answer the first question correctly, the second wrongly
        var questions = loaded.Questions.OrderBy(aq => aq.Order).Select(aq => aq.Question).ToList();
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
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamAsync(service, questions: 2);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var loaded = (await service.GetExamAttemptAsync(attempt.Token))!;
        await service.SubmitExamAsync(attempt.Token, CorrectAnswers(loaded));

        var result = await service.GetExamResultAsync(attempt.Token);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Score);
    }

    [Fact]
    public async Task GetExamResultAsync_BeforeSubmitting_FailsAsInvalid()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamAsync(service);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;

        var result = await service.GetExamResultAsync(attempt.Token);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task GetExamResultAsync_UnknownToken_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.GetExamResultAsync(Guid.NewGuid());

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    // ---------- GetReviewAsync (teacher review) ----------

    private static QuizAttempt SubmittedAttempt(int quizId, string name, string? number, DateTime submittedOn, int score = 1, int max = 2)
        => new()
        {
            Token = Guid.NewGuid(),
            QuizId = quizId,
            StudentName = name,
            StudentNumber = number,
            StartedOn = submittedOn.AddMinutes(-5),
            SubmittedOn = submittedOn,
            Score = score,
            MaxScore = max
        };

    [Fact]
    public async Task GetReviewAsync_ReturnsTheQuizAndOnlySubmittedAttempts_NewestFirst()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamAsync(service);

        var older = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        context.QuizAttempts.Add(SubmittedAttempt(exam.Id, "Анна", "1", older));
        context.QuizAttempts.Add(SubmittedAttempt(exam.Id, "Борис", "2", older.AddHours(1)));
        context.QuizAttempts.Add(new QuizAttempt { Token = Guid.NewGuid(), QuizId = exam.Id, StudentName = "Недовършил", StartedOn = older });
        await context.SaveChangesAsync();

        var result = await service.GetReviewAsync(exam.Id, Owner, isAdministrator: false, page: 1);

        Assert.True(result.Succeeded);
        Assert.Equal(exam.Id, result.Value!.Quiz.Id);
        Assert.Equal(2, result.Value.Attempts.TotalCount);
        Assert.Equal(new[] { "Борис", "Анна" }, result.Value.Attempts.Items.Select(a => a.StudentName));
    }

    [Fact]
    public async Task GetReviewAsync_Missing_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.GetReviewAsync(999, Owner, isAdministrator: false, page: 1);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task GetReviewAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamAsync(service);

        var result = await service.GetReviewAsync(exam.Id, "teacher-2", isAdministrator: false, page: 1);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task GetReviewAsync_ByAdministrator_IsAllowedForAnotherTeachersQuiz()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamAsync(service);

        var result = await service.GetReviewAsync(exam.Id, "teacher-2", isAdministrator: true, page: 1);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task GetReviewAsync_PaginatesTheAttempts()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamAsync(service);
        var baseTime = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++)
        {
            context.QuizAttempts.Add(SubmittedAttempt(exam.Id, "Ученик " + i, i.ToString(), baseTime.AddMinutes(i)));
        }
        await context.SaveChangesAsync();

        var result = await service.GetReviewAsync(exam.Id, Owner, isAdministrator: false, page: 2, pageSize: 2);

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Value!.Attempts.TotalCount);
        Assert.Equal(2, result.Value.Attempts.Page);
        Assert.Equal(3, result.Value.Attempts.TotalPages);
        Assert.Equal(2, result.Value.Attempts.Items.Count);
    }

    // ---------- Proctoring (RecordLeaveAsync + MaxLeaves) ----------

    private static async Task<Quiz> PublishedExamWithLeavesAsync(QuizService service, int? maxLeaves)
    {
        var quiz = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null, maxLeaves), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id, new QuestionInput("В", new[] { "верен", "грешен" }, 0), Owner, false);
        await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        return quiz;
    }

    [Fact]
    public async Task CreateAsync_Exam_StoresTheLeaveAllowance()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null, MaxLeaves: 2), Owner);

        Assert.Equal(2, result.Value!.MaxLeaves);
    }

    [Fact]
    public async Task CreateAsync_Practice_IgnoresTheLeaveAllowance()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.CreateAsync(new QuizInput("Упражнение", null, QuizMode.Practice, null, null, MaxLeaves: 2), Owner);

        Assert.Null(result.Value!.MaxLeaves);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(QuizService.MaxProctorLeaves + 1)]
    public async Task CreateAsync_ExamWithLeaveAllowanceOutOfRange_FailsAsInvalid(int leaves)
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null, leaves), Owner);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task RecordLeaveAsync_IncrementsTheCount_WithinTheAllowance()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamWithLeavesAsync(service, maxLeaves: 2);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;

        var result = await service.RecordLeaveAsync(attempt.Token);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.LeaveCount);
        Assert.False(result.Value.Flagged);
        Assert.False(result.Value.ShouldSubmit);
    }

    [Fact]
    public async Task RecordLeaveAsync_FlagsAndAsksToSubmit_WhenTheAllowanceIsExceeded()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamWithLeavesAsync(service, maxLeaves: 1);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;

        await service.RecordLeaveAsync(attempt.Token);           // 1 — still allowed
        var second = await service.RecordLeaveAsync(attempt.Token); // 2 — over the limit

        Assert.Equal(2, second.Value!.LeaveCount);
        Assert.True(second.Value.Flagged);
        Assert.True(second.Value.ShouldSubmit);
    }

    [Fact]
    public async Task RecordLeaveAsync_WithNoProctoring_NeverFlags()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamWithLeavesAsync(service, maxLeaves: null);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;

        var result = await service.RecordLeaveAsync(attempt.Token);

        Assert.Equal(1, result.Value!.LeaveCount);
        Assert.False(result.Value.Flagged);
        Assert.False(result.Value.ShouldSubmit);
    }

    [Fact]
    public async Task RecordLeaveAsync_AfterSubmitting_DoesNotCount()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await PublishedExamWithLeavesAsync(service, maxLeaves: 1);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        await service.SubmitExamAsync(attempt.Token, new Dictionary<int, int?>());

        var result = await service.RecordLeaveAsync(attempt.Token);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.LeaveCount);
        Assert.False(result.Value.ShouldSubmit);
    }

    [Fact]
    public async Task RecordLeaveAsync_UnknownToken_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.RecordLeaveAsync(Guid.NewGuid());

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    // ---------- Item-based exams (the attempt snapshot) ----------

    // A published exam whose questions come from a "draw N random from a section" item
    private static async Task<Quiz> ItemBasedExamAsync(QuizService service, SchoolLibrary.Data.ApplicationDbContext context, int poolSize, int pick)
    {
        var exam = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null), Owner)).Value!;

        var section = new Section { Name = "Раздел", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();
        for (var i = 1; i <= poolSize; i++)
        {
            context.Questions.Add(new Question
            {
                SectionId = section.Id,
                Text = "Банка " + i,
                Points = 1,
                Options = new List<AnswerOption>
                {
                    new() { Text = "верен", IsCorrect = true, Order = 1 },
                    new() { Text = "грешен", Order = 2 }
                }
            });
        }
        await context.SaveChangesAsync();

        await new QuizItemService(context).AddRandomSectionAsync(exam.Id, section.Id, pick, Owner, false);
        await service.SetPublishedAsync(exam.Id, true, Owner, false);
        return (await service.GetByIdAsync(exam.Id))!;
    }

    [Fact]
    public async Task StartExamAsync_WithItems_SnapshotsTheResolvedQuestions()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await ItemBasedExamAsync(service, context, poolSize: 5, pick: 2);

        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;

        var loaded = (await service.GetExamAttemptAsync(attempt.Token))!;
        Assert.Equal(2, loaded.Questions.Count);           // only the drawn questions
        Assert.Equal(2, loaded.MaxScore);                  // one point each
    }

    [Fact]
    public async Task SubmitExamAsync_WithItems_GradesAgainstTheSnapshot()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var exam = await ItemBasedExamAsync(service, context, poolSize: 5, pick: 2);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var loaded = (await service.GetExamAttemptAsync(attempt.Token))!;

        var answers = loaded.Questions
            .Select(aq => aq.Question)
            .ToDictionary(q => q.Id, q => (int?)q.Options.Single(o => o.IsCorrect).Id);
        var result = await service.SubmitExamAsync(attempt.Token, answers);

        Assert.Equal(2, result.Value!.Score);
        Assert.Equal(2, result.Value.MaxScore);
    }
}
