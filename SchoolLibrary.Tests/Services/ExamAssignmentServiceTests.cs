using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Exams;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class ExamAssignmentServiceTests
{
    private const string Owner = "teacher-1";
    private const string Other = "teacher-2";

    private sealed record Setup(ApplicationDbContext Context, QuizService Quizzes, ExamAssignmentService Exams, Quiz Quiz, SchoolClass SchoolClass);

    private static async Task<Setup> ArrangeAsync(string owner = Owner, int questions = 2, QuizMode mode = QuizMode.Practice)
    {
        var context = TestDbContextFactory.Create();
        var quizzes = new QuizService(context, new QuizItemService(context));
        var quiz = (await quizzes.CreateAsync(new QuizInput("Дроби", null, mode, mode == QuizMode.Exam ? 30 : null, null), owner)).Value!;
        for (var i = 0; i < questions; i++)
        {
            await quizzes.AddQuestionAsync(quiz.Id, new QuestionInput("Въпрос " + i, new[] { "верен", "грешен" }, 0), owner, false);
        }

        var schoolClass = (await new SchoolClassService(context).CreateAsync("5А", owner)).Value!;
        return new Setup(context, quizzes, new ExamAssignmentService(context), quiz, schoolClass);
    }

    private static Task<ServiceResult<ExamAssignment>> AssignAsync(Setup s, string user = Owner, bool admin = false, int? minutes = 20, int? leaves = 2)
        => s.Exams.CreateAsync(new ExamAssignmentInput(s.Quiz.Id, s.SchoolClass.Id, minutes, leaves), user, admin);

    private static QuizAttempt Attempt(ExamAssignment exam, string number, bool submitted, int score = 1)
        => new()
        {
            Token = Guid.NewGuid(),
            QuizId = exam.QuizId,
            ExamAssignmentId = exam.Id,
            StudentName = "Ученик " + number,
            StudentNumber = number,
            StartedOn = DateTime.UtcNow,
            SubmittedOn = submitted ? DateTime.UtcNow : null,
            Score = score,
            MaxScore = 2
        };

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_GivesTheQuizToTheClassOpenWithHiddenResultsAndACode()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var result = await AssignAsync(s);

        Assert.True(result.Succeeded);
        var exam = result.Value!;
        Assert.Equal(QuizService.CodeLength, exam.Code.Length);
        Assert.True(exam.IsOpen);
        Assert.False(exam.ResultsReleased);
        Assert.Equal(20, exam.TimeLimitMinutes);
        Assert.Equal(2, exam.MaxLeaves);
        Assert.True(exam.CodeHints);
        Assert.True(exam.OneQuestionPerPage);
        Assert.Equal(DateTimeKind.Utc, exam.CreatedOn.Kind);
    }

    [Fact]
    public async Task CreateAsync_WithoutCodeHints_KeepsTheChoice()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var result = await s.Exams.CreateAsync(new ExamAssignmentInput(s.Quiz.Id, s.SchoolClass.Id, 20, null, CodeHints: false), Owner, false);

        Assert.False(result.Value!.CodeHints);
        Assert.False(s.Context.ExamAssignments.Single().CodeHints);
    }

    [Fact]
    public async Task CreateAsync_AnUnpublishedPracticeQuiz_CanBeAssigned()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        Assert.False(s.Quiz.IsPublished);

        Assert.True((await AssignAsync(s)).Succeeded);
    }

    [Fact]
    public async Task CreateAsync_TwoAssignments_GetDifferentCodes()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var a = (await AssignAsync(s)).Value!;
        var b = (await AssignAsync(s)).Value!;

        Assert.NotEqual(a.Code, b.Code);
    }

    [Fact]
    public async Task CreateAsync_AnotherTeachersClassOrQuiz_IsForbidden()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        Assert.Equal(ServiceError.Forbidden, (await AssignAsync(s, user: Other)).Error);
    }

    [Fact]
    public async Task CreateAsync_ByAnAdministrator_IsAllowed()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        Assert.True((await AssignAsync(s, user: "admin", admin: true)).Succeeded);
    }

    [Fact]
    public async Task CreateAsync_AnExamModeQuiz_IsInvalid()
    {
        var s = await ArrangeAsync(mode: QuizMode.Exam);
        using var _ = s.Context;

        Assert.Equal(ServiceError.Invalid, (await AssignAsync(s)).Error);
    }

    [Fact]
    public async Task CreateAsync_AQuizWithoutQuestions_IsInvalid()
    {
        var s = await ArrangeAsync(questions: 0);
        using var _ = s.Context;

        Assert.Equal(ServiceError.Invalid, (await AssignAsync(s)).Error);
    }

    [Fact]
    public async Task CreateAsync_AQuizWithOnlyBankItems_CanBeAssigned()
    {
        var s = await ArrangeAsync(questions: 0);
        using var _ = s.Context;
        var section = new Section { Name = "Банка", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        s.Context.Sections.Add(section);
        await s.Context.SaveChangesAsync();
        s.Context.QuizItems.Add(new QuizItem { QuizId = s.Quiz.Id, Kind = QuizItemKind.RandomFromSection, SectionId = section.Id, PickCount = 3 });
        await s.Context.SaveChangesAsync();

        Assert.True((await AssignAsync(s)).Succeeded);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(601, null)]
    [InlineData(null, -1)]
    [InlineData(null, 101)]
    public async Task CreateAsync_SettingsOutOfRange_AreInvalid(int? minutes, int? leaves)
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        Assert.Equal(ServiceError.Invalid, (await AssignAsync(s, minutes: minutes, leaves: leaves)).Error);
    }

    [Fact]
    public async Task CreateAsync_MissingClassOrQuiz_IsNotFound()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        Assert.Equal(ServiceError.NotFound, (await s.Exams.CreateAsync(new ExamAssignmentInput(s.Quiz.Id, 999, null, null), Owner, false)).Error);
        Assert.Equal(ServiceError.NotFound, (await s.Exams.CreateAsync(new ExamAssignmentInput(999, s.SchoolClass.Id, null, null), Owner, false)).Error);
    }

    // ---------- Lists ----------

    [Fact]
    public async Task GetForOwnerAsync_CountsSubmittedAndInProgressAttempts()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;
        s.Context.QuizAttempts.AddRange(Attempt(exam, "1", true), Attempt(exam, "2", true), Attempt(exam, "3", false));
        await s.Context.SaveChangesAsync();

        var item = Assert.Single(await s.Exams.GetForOwnerAsync(Owner));

        Assert.Equal(2, item.SubmittedCount);
        Assert.Equal(1, item.InProgressCount);
        Assert.Equal("Дроби", item.Exam.Quiz.Title);
        Assert.Equal("5А", item.Exam.SchoolClass.Name);
    }

    [Fact]
    public async Task GetForOwnerAsync_ScopesByOwnerAndClass()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        await AssignAsync(s);
        var otherClass = (await new SchoolClassService(s.Context).CreateAsync("6Б", Owner)).Value!;
        await s.Exams.CreateAsync(new ExamAssignmentInput(s.Quiz.Id, otherClass.Id, null, null), Owner, false);

        Assert.Equal(2, (await s.Exams.GetForOwnerAsync(Owner)).Count);
        Assert.Single(await s.Exams.GetForOwnerAsync(Owner, otherClass.Id));
        Assert.Empty(await s.Exams.GetForOwnerAsync(Other));
        Assert.Equal(2, (await s.Exams.GetForOwnerAsync(null)).Count);
    }

    [Fact]
    public async Task GetAssignableQuizzesAsync_ReturnsTheOwnersPracticeQuizzesWithQuestions()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        await s.Quizzes.CreateAsync(new QuizInput("Празен", null, QuizMode.Practice, null, null), Owner);
        var exam = (await s.Quizzes.CreateAsync(new QuizInput("Стар изпит", null, QuizMode.Exam, 10, null), Owner)).Value!;
        await s.Quizzes.AddQuestionAsync(exam.Id, new QuestionInput("В", new[] { "а", "б" }, 0), Owner, false);

        var quizzes = await s.Exams.GetAssignableQuizzesAsync(Owner);

        Assert.Equal(new[] { "Дроби" }, quizzes.Select(q => q.Title));
        Assert.Empty(await s.Exams.GetAssignableQuizzesAsync(Other));
    }

    [Fact]
    public async Task GetByIdAsync_LoadsTheQuizAndClass_OrReturnsNull()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;

        var loaded = await s.Exams.GetByIdAsync(exam.Id);

        Assert.Equal("Дроби", loaded!.Quiz.Title);
        Assert.Equal("5А", loaded.SchoolClass.Name);
        Assert.Null(await s.Exams.GetByIdAsync(999));
    }

    // ---------- Changes ----------

    [Fact]
    public async Task UpdateSettingsAsync_ChangesTheTimeAndProctoring()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;

        var result = await s.Exams.UpdateSettingsAsync(exam.Id, new ExamSettingsInput(null, 0), Owner, false);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.TimeLimitMinutes);
        Assert.Equal(0, result.Value.MaxLeaves);
        Assert.True(result.Value.CodeHints);
    }

    [Fact]
    public async Task UpdateSettingsAsync_TurnsCodeHintsOffAndOn()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;

        await s.Exams.UpdateSettingsAsync(exam.Id, new ExamSettingsInput(20, 2, CodeHints: false), Owner, false);
        Assert.False(s.Context.ExamAssignments.Single().CodeHints);

        await s.Exams.UpdateSettingsAsync(exam.Id, new ExamSettingsInput(20, 2, CodeHints: true), Owner, false);
        Assert.True(s.Context.ExamAssignments.Single().CodeHints);
    }

    [Fact]
    public async Task CreateAndUpdate_KeepTheOneQuestionPerPageChoice()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var exam = (await s.Exams.CreateAsync(new ExamAssignmentInput(s.Quiz.Id, s.SchoolClass.Id, 20, null, OneQuestionPerPage: false), Owner, false)).Value!;
        Assert.False(s.Context.ExamAssignments.Single().OneQuestionPerPage);

        await s.Exams.UpdateSettingsAsync(exam.Id, new ExamSettingsInput(20, null, OneQuestionPerPage: true), Owner, false);
        Assert.True(s.Context.ExamAssignments.Single().OneQuestionPerPage);
    }

    [Fact]
    public async Task UpdateSettingsAsync_OutOfRange_KeepsCodeHints()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;

        await s.Exams.UpdateSettingsAsync(exam.Id, new ExamSettingsInput(0, null, CodeHints: false), Owner, false);

        Assert.True(s.Context.ExamAssignments.Single().CodeHints);
    }

    [Fact]
    public void QuizAttempt_CodeHints_FollowsTheClassExamAndIsOnForAQuizExam()
    {
        Assert.True(new QuizAttempt { Quiz = new Quiz() }.CodeHints);
        Assert.True(new QuizAttempt { ExamAssignment = new ExamAssignment { CodeHints = true } }.CodeHints);
        Assert.False(new QuizAttempt { ExamAssignment = new ExamAssignment { CodeHints = false } }.CodeHints);
    }

    [Fact]
    public async Task UpdateSettingsAsync_OutOfRange_IsInvalid()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;

        Assert.Equal(ServiceError.Invalid, (await s.Exams.UpdateSettingsAsync(exam.Id, new ExamSettingsInput(0, null), Owner, false)).Error);
    }

    [Fact]
    public async Task SetOpenAndSetResultsReleased_ChangeTheFlags()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;

        await s.Exams.SetOpenAsync(exam.Id, false, Owner, false);
        await s.Exams.SetResultsReleasedAsync(exam.Id, true, Owner, false);

        var loaded = await s.Exams.GetByIdAsync(exam.Id);
        Assert.False(loaded!.IsOpen);
        Assert.True(loaded.ResultsReleased);
    }

    [Fact]
    public async Task Changes_ByAnotherTeacher_AreForbidden_AndOnAMissingExam_NotFound()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;

        Assert.Equal(ServiceError.Forbidden, (await s.Exams.SetOpenAsync(exam.Id, false, Other, false)).Error);
        Assert.Equal(ServiceError.Forbidden, (await s.Exams.SetResultsReleasedAsync(exam.Id, true, Other, false)).Error);
        Assert.Equal(ServiceError.Forbidden, (await s.Exams.UpdateSettingsAsync(exam.Id, new ExamSettingsInput(5, null), Other, false)).Error);
        Assert.Equal(ServiceError.Forbidden, (await s.Exams.DeleteAsync(exam.Id, Other, false)).Error);
        Assert.Equal(ServiceError.NotFound, (await s.Exams.SetOpenAsync(999, false, Owner, false)).Error);
        Assert.Equal(ServiceError.NotFound, (await s.Exams.DeleteAsync(999, Owner, false)).Error);
    }

    [Fact]
    public async Task DeleteAsync_ByOwner_RemovesTheExam()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;

        Assert.True((await s.Exams.DeleteAsync(exam.Id, Owner, false)).Succeeded);
        Assert.Null(await s.Exams.GetByIdAsync(exam.Id));
    }

    // ---------- Results ----------

    [Fact]
    public async Task GetResultsAsync_ListsEveryAttemptInClassNumberOrder()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;
        s.Context.QuizAttempts.AddRange(Attempt(exam, "10", true), Attempt(exam, "2", false), Attempt(exam, "1", true));
        await s.Context.SaveChangesAsync();

        var result = await s.Exams.GetResultsAsync(exam.Id, Owner, false, 1);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "1", "2", "10" }, result.Value!.Attempts.Items.Select(a => a.StudentNumber));
        Assert.Equal(3, result.Value.Attempts.TotalCount);
    }

    [Fact]
    public async Task GetResultsAsync_PagesTheAttempts()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;
        for (var i = 1; i <= 5; i++)
        {
            s.Context.QuizAttempts.Add(Attempt(exam, i.ToString(), true));
        }
        await s.Context.SaveChangesAsync();

        var page = (await s.Exams.GetResultsAsync(exam.Id, Owner, false, page: 2, pageSize: 2)).Value!.Attempts;

        Assert.Equal(new[] { "3", "4" }, page.Items.Select(a => a.StudentNumber));
        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public async Task GetResultsAsync_AnotherTeacher_IsForbidden_Missing_IsNotFound()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;

        Assert.Equal(ServiceError.Forbidden, (await s.Exams.GetResultsAsync(exam.Id, Other, false, 1)).Error);
        Assert.Equal(ServiceError.NotFound, (await s.Exams.GetResultsAsync(999, Owner, false, 1)).Error);
    }

    [Fact]
    public async Task GetAttemptReviewAsync_ReturnsTheGradedAnswersOfASubmittedAttempt()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;
        var token = (await s.Quizzes.StartExamAsync(exam.Code, "Иван", "3")).Value!.Token;
        var attempt = await s.Quizzes.GetExamAttemptAsync(token);
        var correct = attempt!.Questions.ToDictionary(
            aq => aq.QuestionId, aq => (int?)aq.Question.Options.Single(o => o.IsCorrect).Id);
        await s.Quizzes.SubmitExamAsync(token, SubmittedAnswer.FromChoices(correct));

        var result = await s.Exams.GetAttemptReviewAsync(attempt.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Result.Score);
        Assert.Equal("Иван", result.Value.Attempt.StudentName);
        Assert.Equal("5А", result.Value.Attempt.ExamAssignment!.SchoolClass.Name);
    }

    [Fact]
    public async Task GetAttemptReviewAsync_InProgress_IsInvalid_AnotherTeacher_IsForbidden()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await AssignAsync(s)).Value!;
        var attempt = (await s.Quizzes.StartExamAsync(exam.Code, "Иван", "3")).Value!;

        Assert.Equal(ServiceError.Invalid, (await s.Exams.GetAttemptReviewAsync(attempt.Id, Owner, false)).Error);
        Assert.Equal(ServiceError.Forbidden, (await s.Exams.GetAttemptReviewAsync(attempt.Id, Other, false)).Error);
    }

    [Fact]
    public async Task GetAttemptReviewAsync_AQuizModeAttemptOrMissing_IsNotFound()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var legacy = new QuizAttempt { Token = Guid.NewGuid(), QuizId = s.Quiz.Id, StudentName = "Иван", StartedOn = DateTime.UtcNow };
        s.Context.QuizAttempts.Add(legacy);
        await s.Context.SaveChangesAsync();

        Assert.Equal(ServiceError.NotFound, (await s.Exams.GetAttemptReviewAsync(legacy.Id, Owner, false)).Error);
        Assert.Equal(ServiceError.NotFound, (await s.Exams.GetAttemptReviewAsync(999, Owner, false)).Error);
    }
}
