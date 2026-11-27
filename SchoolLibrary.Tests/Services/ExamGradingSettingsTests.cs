using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Exams;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>How a class exam grades: its method and scale, chosen when assigning and in the settings.</summary>
public class ExamGradingSettingsTests
{
    private const string Owner = "teacher-1";

    private sealed record Setup(ApplicationDbContext Context, QuizService Quizzes, ExamAssignmentService Exams,
        int QuizId, int ClassId, GradeScale Scale);

    // A quiz of 4 one-point questions, a class and a scale of the teacher (3 from 40%, 4 from 60%, 5 from 75%, 6 from 90%)
    private static async Task<Setup> ArrangeAsync()
    {
        var context = TestDbContextFactory.Create();
        var quizzes = new QuizService(context, new QuizItemService(context));
        var quiz = (await quizzes.CreateAsync(new QuizInput("Дроби", null, QuizMode.Practice, null, null), Owner)).Value!;
        for (var i = 0; i < 4; i++)
        {
            await quizzes.AddQuestionAsync(quiz.Id, new QuestionInput("Въпрос " + i, new[] { "верен", "грешен" }, 0), Owner, false);
        }

        var schoolClass = (await new SchoolClassService(context).CreateAsync("5А", Owner)).Value!;
        var scale = (await new GradeScaleService(context).CreateAsync(new GradeScaleInput("Строга", 40m, 60m, 75m, 90m), Owner)).Value!;
        return new Setup(context, quizzes, new ExamAssignmentService(context), quiz.Id, schoolClass.Id, scale);
    }

    private static ExamAssignmentInput Assign(Setup s, GradingMethod method = GradingMethod.Thresholds, int? scaleId = null)
        => new(s.QuizId, s.ClassId, null, null, GradingMethod: method, GradeScaleId: scaleId);

    // A student who answers the first <paramref name="right"/> of the 4 questions correctly
    private static async Task<QuizResult> SubmitAsync(Setup s, ExamAssignment exam, int right, string number = "1")
    {
        var attempt = (await s.Quizzes.StartExamAsync(exam.Code, "Ученик " + number, number)).Value!;
        var questions = await s.Context.Questions.Include(q => q.Options).OrderBy(q => q.Order).ToListAsync();
        var answers = questions.Take(right).ToDictionary(q => q.Id, q => (int?)q.Options.Single(o => o.IsCorrect).Id);
        return (await s.Quizzes.SubmitExamAsync(attempt.Token, SubmittedAnswer.FromChoices(answers))).Value!;
    }

    // ---------- Assigning ----------

    [Fact]
    public async Task CreateAsync_WithTheTeachersScale_StoresIt()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var result = await s.Exams.CreateAsync(Assign(s, scaleId: s.Scale.Id), Owner, false);

        Assert.True(result.Succeeded, result.Message);
        var exam = await s.Context.ExamAssignments.AsNoTracking().SingleAsync();
        Assert.Equal(GradingMethod.Thresholds, exam.GradingMethod);
        Assert.Equal(s.Scale.Id, exam.GradeScaleId);
        Assert.Equal("Строга", exam.GradeScale!.Name);   // loaded with the exam
    }

    [Fact]
    public async Task CreateAsync_WithAFormula_KeepsNoScale()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        await s.Exams.CreateAsync(Assign(s, GradingMethod.TimesSix, s.Scale.Id), Owner, false);

        var exam = await s.Context.ExamAssignments.AsNoTracking().SingleAsync();
        Assert.Equal(GradingMethod.TimesSix, exam.GradingMethod);
        Assert.Null(exam.GradeScaleId);
    }

    [Fact]
    public async Task CreateAsync_WithAnotherTeachersScale_IsInvalid()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var foreign = (await new GradeScaleService(s.Context).CreateAsync(new GradeScaleInput("Чужда", 10m, 20m, 30m, 40m), "teacher-2")).Value!;

        var result = await s.Exams.CreateAsync(Assign(s, scaleId: foreign.Id), Owner, false);
        var byAdmin = await s.Exams.CreateAsync(Assign(s, scaleId: foreign.Id), "admin", true);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Изберете една от скалите на учителя на класа.", result.Message);
        Assert.Equal(ServiceError.Invalid, byAdmin.Error);   // the scale must still be the class teacher's
        Assert.Empty(s.Context.ExamAssignments);
    }

    [Fact]
    public async Task CreateAsync_UnknownMethod_IsInvalid()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var result = await s.Exams.CreateAsync(Assign(s, (GradingMethod)7), Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Изберете начин на оценяване.", result.Message);
    }

    // ---------- Grades ----------

    [Fact]
    public async Task SystemScale_IsTheDefault()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await s.Exams.CreateAsync(Assign(s), Owner, false)).Value!;

        var result = await SubmitAsync(s, exam, right: 2);   // 50% → a 4 on the system scale

        Assert.Equal(4, result.Grade);
        Assert.Equal("4", result.GradeText);
    }

    [Fact]
    public async Task TheExamsScale_GradesTheAttempt()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await s.Exams.CreateAsync(Assign(s, scaleId: s.Scale.Id), Owner, false)).Value!;

        var result = await SubmitAsync(s, exam, right: 2);   // 50% → a 3 on the strict scale

        Assert.Equal(3, result.Grade);
        Assert.Equal("Среден", result.GradeLabel);
    }

    [Theory]
    [InlineData(GradingMethod.TimesSix, 3, "4,50", 5)]      // 3/4 × 6
    [InlineData(GradingMethod.TwoPlusFour, 3, "5,00", 5)]   // 2 + 4 × 3/4
    [InlineData(GradingMethod.TimesSix, 1, "2", 2)]         // 1,50 → a 2
    [InlineData(GradingMethod.TwoPlusFour, 1, "3,00", 3)]
    public async Task AFormula_GradesWithTwoDecimals(GradingMethod method, int right, string shown, int whole)
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await s.Exams.CreateAsync(Assign(s, method), Owner, false)).Value!;

        var result = await SubmitAsync(s, exam, right);

        Assert.Equal(shown, result.GradeText);
        Assert.Equal(whole, result.Grade);
    }

    [Fact]
    public async Task ChangingTheMethod_RegradesAttemptsAlreadyMade_AndKeepsTheOtherSettings()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await s.Exams.CreateAsync(new ExamAssignmentInput(s.QuizId, s.ClassId, 20, 2), Owner, false)).Value!;
        var attempt = await SubmitAsync(s, exam, right: 3);
        Assert.Equal("5", attempt.GradeText);   // 75% on the system scale

        var update = await s.Exams.UpdateSettingsAsync(exam.Id,
            new ExamSettingsInput(20, 2, GradingMethod: GradingMethod.TimesSix), Owner, false);
        s.Context.ChangeTracker.Clear();

        Assert.True(update.Succeeded, update.Message);
        var review = (await s.Exams.GetAttemptReviewAsync(
            (await s.Context.QuizAttempts.SingleAsync()).Id, Owner, false)).Value!;
        Assert.Equal("4,50", review.Result.GradeText);
        var saved = await s.Context.ExamAssignments.AsNoTracking().SingleAsync();
        Assert.Equal((20, 2), (saved.TimeLimitMinutes, saved.MaxLeaves));
    }

    [Fact]
    public async Task ChangingTheScale_ChangesTheGradeOfItsExams()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await s.Exams.CreateAsync(Assign(s, scaleId: s.Scale.Id), Owner, false)).Value!;
        await SubmitAsync(s, exam, right: 2);   // 50%: a 3 on the strict scale

        await new GradeScaleService(s.Context).UpdateAsync(s.Scale.Id, new GradeScaleInput("Строга", 20m, 50m, 75m, 90m), Owner, false);
        s.Context.ChangeTracker.Clear();

        var review = (await s.Exams.GetAttemptReviewAsync(
            (await s.Context.QuizAttempts.SingleAsync()).Id, Owner, false)).Value!;
        Assert.Equal(4, review.Result.Grade);
    }

    [Fact]
    public async Task UpdateSettingsAsync_WithAnotherTeachersScale_IsInvalid_AndChangesNothing()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var exam = (await s.Exams.CreateAsync(Assign(s, GradingMethod.TwoPlusFour), Owner, false)).Value!;
        var foreign = (await new GradeScaleService(s.Context).CreateAsync(new GradeScaleInput("Чужда", 10m, 20m, 30m, 40m), "teacher-2")).Value!;

        var result = await s.Exams.UpdateSettingsAsync(exam.Id, new ExamSettingsInput(null, null, GradeScaleId: foreign.Id), Owner, false);
        s.Context.ChangeTracker.Clear();

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal(GradingMethod.TwoPlusFour, (await s.Context.ExamAssignments.SingleAsync()).GradingMethod);
    }

    // ---------- ExamGrading.For ----------

    [Fact]
    public void For_WithoutAnExam_OrScale_IsTheSystemScale()
    {
        Assert.Same(ExamGrading.Default, ExamGrading.For(null));
        Assert.Same(ExamGrading.Default, ExamGrading.For(new ExamAssignment()));
        Assert.Equal(GradingMethod.TwoPlusFour, ExamGrading.For(new ExamAssignment { GradingMethod = GradingMethod.TwoPlusFour }).Method);
        Assert.Equal(40m, ExamGrading.For(new ExamAssignment { GradeScale = new GradeScale { Three = 40m, Four = 60m, Five = 75m, Six = 90m } }).Three);
    }
}
