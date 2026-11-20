using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Exams;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

// The student side of a class exam: entry by code, then name and class number; results hidden until released.
public class ClassExamTests
{
    private const string Owner = "teacher-1";

    private sealed record Setup(ApplicationDbContext Context, QuizService Quizzes, ExamAssignmentService Exams, ExamAssignment Exam);

    private static async Task<Setup> ArrangeAsync(int? minutes = 15, int? leaves = 1)
    {
        var context = TestDbContextFactory.Create();
        var quizzes = new QuizService(context, new QuizItemService(context));
        var quiz = (await quizzes.CreateAsync(new QuizInput("Дроби", null, QuizMode.Practice, null, null), Owner)).Value!;
        for (var i = 0; i < 2; i++)
        {
            await quizzes.AddQuestionAsync(quiz.Id, new QuestionInput("Въпрос " + i, new[] { "верен", "грешен" }, 0), Owner, false);
        }

        var schoolClass = (await new SchoolClassService(context).CreateAsync("5А", Owner)).Value!;
        var exams = new ExamAssignmentService(context);
        var exam = (await exams.CreateAsync(new ExamAssignmentInput(quiz.Id, schoolClass.Id, minutes, leaves), Owner, false)).Value!;
        return new Setup(context, quizzes, exams, exam);
    }

    private static async Task SubmitAsync(Setup s, Guid token)
        => await s.Quizzes.SubmitExamAsync(token, new Dictionary<int, int?>());

    // ---------- FindExamAsync ----------

    [Fact]
    public async Task FindExamAsync_ClassExam_ReturnsTheTitleClassAndState()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var result = await s.Quizzes.FindExamAsync("  " + s.Exam.Code.ToLower() + " ");

        Assert.True(result.Succeeded);
        Assert.Equal(new ExamEntry(s.Exam.Code, "Дроби", "5А", 15, true), result.Value);
    }

    [Fact]
    public async Task FindExamAsync_ClosedClassExam_IsStillFound_SoStudentsCanSeeTheirResult()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        await s.Exams.SetOpenAsync(s.Exam.Id, false, Owner, false);

        var result = await s.Quizzes.FindExamAsync(s.Exam.Code);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsOpen);
    }

    [Fact]
    public async Task FindExamAsync_ExamModeQuiz_IsFoundWithoutAClass()
    {
        using var context = TestDbContextFactory.Create();
        var quizzes = new QuizService(context, new QuizItemService(context));
        var quiz = (await quizzes.CreateAsync(new QuizInput("Стар", null, QuizMode.Exam, 20, null), Owner)).Value!;
        await quizzes.AddQuestionAsync(quiz.Id, new QuestionInput("В", new[] { "а", "б" }, 0), Owner, false);
        await quizzes.SetPublishedAsync(quiz.Id, true, Owner, false);

        var result = await quizzes.FindExamAsync(quiz.Code!);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.ClassName);
        Assert.Equal("Стар", result.Value.Title);
    }

    [Fact]
    public async Task FindExamAsync_UnknownCode_IsNotFound()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        Assert.Equal(ServiceError.NotFound, (await s.Quizzes.FindExamAsync("ZZZZZZ")).Error);
    }

    // ---------- StartExamAsync ----------

    [Fact]
    public async Task StartExamAsync_CreatesAnAttemptLinkedToTheClassExam()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var result = await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван Петров", "07");

        Assert.True(result.Succeeded);
        var attempt = result.Value!;
        Assert.Equal(s.Exam.Id, attempt.ExamAssignmentId);
        Assert.Equal(s.Exam.QuizId, attempt.QuizId);
        Assert.Equal("7", attempt.StudentNumber); // stored without leading zeros
        Assert.Equal(2, attempt.Questions.Count);
        Assert.Equal(2, attempt.MaxScore);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("пет")]
    [InlineData("0")]
    [InlineData("100")]
    public async Task StartExamAsync_WithoutAValidClassNumber_IsInvalid(string? number)
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var result = await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", number);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task StartExamAsync_SameNumberAndName_ResumesTheAttempt_IgnoringCaseAndSpaces()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var first = (await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван Петров", "5")).Value!;

        var again = await s.Quizzes.StartExamAsync(s.Exam.Code, "  иван   петров ", "5");

        Assert.True(again.Succeeded);
        Assert.Equal(first.Token, again.Value!.Token);
    }

    [Fact]
    public async Task StartExamAsync_SameNumberWithAnotherName_IsAConflict()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5");

        var result = await s.Quizzes.StartExamAsync(s.Exam.Code, "Мария", "5");

        Assert.Equal(ServiceError.Conflict, result.Error);
    }

    [Fact]
    public async Task StartExamAsync_AfterSubmitting_ReturnsTheSubmittedAttempt_ForTheResult()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var token = (await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5")).Value!.Token;
        await SubmitAsync(s, token);

        var again = await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5");

        Assert.True(again.Succeeded);
        Assert.Equal(token, again.Value!.Token);
        Assert.NotNull(again.Value.SubmittedOn);
    }

    [Fact]
    public async Task StartExamAsync_ClosedExam_RefusesNewStudents_ButLetsAStartedOneBackIn()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var started = (await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5")).Value!;
        await s.Exams.SetOpenAsync(s.Exam.Id, false, Owner, false);

        var newcomer = await s.Quizzes.StartExamAsync(s.Exam.Code, "Мария", "6");
        var back = await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5");

        Assert.Equal(ServiceError.Conflict, newcomer.Error);
        Assert.Equal(started.Token, back.Value!.Token);
    }

    // ---------- Settings in force ----------

    [Fact]
    public async Task GetExamAttemptAsync_CarriesTheClassExamTimeLimitAndProctoring()
    {
        var s = await ArrangeAsync(minutes: 15, leaves: 1);
        using var _ = s.Context;
        var token = (await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5")).Value!.Token;

        var attempt = await s.Quizzes.GetExamAttemptAsync(token);

        Assert.Equal(15, attempt!.TimeLimitMinutes);
        Assert.Equal(1, attempt.MaxLeaves);
        Assert.Null(attempt.Quiz.TimeLimitMinutes); // the practice quiz itself has no timer
        Assert.Equal("5А", attempt.ExamAssignment!.SchoolClass.Name);
    }

    [Fact]
    public async Task RecordLeaveAsync_UsesTheClassExamAllowance()
    {
        var s = await ArrangeAsync(leaves: 1);
        using var _ = s.Context;
        var token = (await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5")).Value!.Token;

        var first = (await s.Quizzes.RecordLeaveAsync(token)).Value!;
        var second = (await s.Quizzes.RecordLeaveAsync(token)).Value!;

        Assert.False(first.ShouldSubmit);
        Assert.True(second.ShouldSubmit);
        Assert.True(second.Flagged);
    }

    // ---------- Result visibility ----------

    [Fact]
    public async Task GetExamResultAsync_IsHiddenUntilTheTeacherReleasesIt()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var token = (await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5")).Value!.Token;
        await SubmitAsync(s, token);

        var hidden = await s.Quizzes.GetExamResultAsync(token);
        await s.Exams.SetResultsReleasedAsync(s.Exam.Id, true, Owner, false);
        var shown = await s.Quizzes.GetExamResultAsync(token);

        Assert.Equal(ServiceError.Forbidden, hidden.Error);
        Assert.True(shown.Succeeded);
        Assert.Equal(2, shown.Value!.Questions.Count);
    }

    // ---------- Deleting an attempt (a retake) ----------

    [Fact]
    public async Task DeleteAttemptAsync_LetsTheStudentTakeTheExamAgainWithTheSameNumber()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var first = (await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5")).Value!;
        await SubmitAsync(s, first.Token);

        var deleted = await s.Exams.DeleteAttemptAsync(first.Id, Owner, false);
        var again = await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5");

        Assert.True(deleted.Succeeded);
        Assert.Equal(s.Exam.Id, deleted.Value!.ExamAssignmentId);
        Assert.True(again.Succeeded);
        Assert.NotEqual(first.Token, again.Value!.Token);
        Assert.Null(again.Value.SubmittedOn);
        Assert.Null(await s.Quizzes.GetExamAttemptAsync(first.Token)); // the old link no longer opens
    }

    [Fact]
    public async Task DeleteAttemptAsync_AlsoFreesTheNumberForAnotherName()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var wrong = (await s.Quizzes.StartExamAsync(s.Exam.Code, "Грешно име", "5")).Value!;

        await s.Exams.DeleteAttemptAsync(wrong.Id, Owner, false);

        Assert.True((await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5")).Succeeded);
    }

    [Fact]
    public async Task DeleteAttemptAsync_AnotherTeacher_IsForbidden_AndTheAttemptStays()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var attempt = (await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5")).Value!;

        var result = await s.Exams.DeleteAttemptAsync(attempt.Id, "teacher-2", false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
        Assert.NotNull(await s.Quizzes.GetExamAttemptAsync(attempt.Token));
    }

    [Fact]
    public async Task DeleteAttemptAsync_ByAnAdministrator_IsAllowed()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var attempt = (await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5")).Value!;

        Assert.True((await s.Exams.DeleteAttemptAsync(attempt.Id, "admin", true)).Succeeded);
    }

    [Fact]
    public async Task DeleteAttemptAsync_MissingOrAQuizModeAttempt_IsNotFound()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var legacy = new QuizAttempt { Token = Guid.NewGuid(), QuizId = s.Exam.QuizId, StudentName = "Иван", StartedOn = DateTime.UtcNow };
        s.Context.QuizAttempts.Add(legacy);
        await s.Context.SaveChangesAsync();

        Assert.Equal(ServiceError.NotFound, (await s.Exams.DeleteAttemptAsync(legacy.Id, Owner, false)).Error);
        Assert.Equal(ServiceError.NotFound, (await s.Exams.DeleteAttemptAsync(999, Owner, false)).Error);
    }

    [Fact]
    public async Task GetReviewAsync_OfTheQuiz_LeavesOutClassExamAttempts()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var token = (await s.Quizzes.StartExamAsync(s.Exam.Code, "Иван", "5")).Value!.Token;
        await SubmitAsync(s, token);

        var review = await s.Quizzes.GetReviewAsync(s.Exam.QuizId, Owner, false, 1);

        Assert.Equal(0, review.Value!.Attempts.TotalCount);
    }
}
