using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Exams;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>A teacher's own grade scales: thresholds, names, rights and scales in use.</summary>
public class GradeScaleServiceTests
{
    private const string Owner = "teacher-1";

    private static GradeScaleInput Input(string name = "Строга", decimal three = 40m, decimal four = 60m,
        decimal five = 75m, decimal six = 90m) => new(name, three, four, five, six);

    // An exam of the owner that uses the scale
    private static async Task UseInAnExamAsync(ApplicationDbContext context, int scaleId)
    {
        var quizzes = new QuizService(context, new QuizItemService(context));
        var quiz = (await quizzes.CreateAsync(new QuizInput("Дроби", null, QuizMode.Practice, null, null), Owner)).Value!;
        await quizzes.AddQuestionAsync(quiz.Id, new QuestionInput("1/2 + 1/2 = ?", new[] { "1", "2" }, 0), Owner, false);
        var schoolClass = (await new SchoolClassService(context).CreateAsync("5А", Owner)).Value!;
        var exam = (await new ExamAssignmentService(context).CreateAsync(
            new ExamAssignmentInput(quiz.Id, schoolClass.Id, null, null), Owner, false)).Value!;
        var tracked = await context.ExamAssignments.SingleAsync(e => e.Id == exam.Id);
        tracked.GradeScaleId = scaleId;
        await context.SaveChangesAsync();
    }

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_SavesTheNameAndThresholds()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeScaleService(context);

        var result = await service.CreateAsync(Input("  За   5. клас ", 33.33m, 50m, 66.67m, 90m), Owner);

        Assert.True(result.Succeeded, result.Message);
        var scale = await context.GradeScales.SingleAsync();
        Assert.Equal("За 5. клас", scale.Name);
        Assert.Equal((33.33m, 50m, 66.67m, 90m), (scale.Three, scale.Four, scale.Five, scale.Six));
        Assert.Equal(Owner, scale.OwnerId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateAsync_WithoutAName_IsInvalid(string? name)
    {
        using var context = TestDbContextFactory.Create();

        var result = await new GradeScaleService(context).CreateAsync(Input() with { Name = name }, Owner);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Въведете име на скалата.", result.Message);
    }

    [Fact]
    public async Task CreateAsync_TooLongName_IsInvalid()
    {
        using var context = TestDbContextFactory.Create();

        var result = await new GradeScaleService(context).CreateAsync(Input(new string('а', GradeScale.NameMaxLength + 1)), Owner);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task CreateAsync_ThresholdsNotRising_IsInvalid()
    {
        using var context = TestDbContextFactory.Create();

        var result = await new GradeScaleService(context).CreateAsync(Input(four: 30m), Owner);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Всеки праг трябва да е по-висок от предишния (за 3 < за 4 < за 5 < за 6).", result.Message);
        Assert.Empty(context.GradeScales);
    }

    [Fact]
    public async Task CreateAsync_SameNameForTheSameTeacher_IsAConflict_ButAnotherTeacherMayUseIt()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeScaleService(context);
        await service.CreateAsync(Input("Строга"), Owner);

        var again = await service.CreateAsync(Input("строга"), Owner);
        var other = await service.CreateAsync(Input("Строга"), "teacher-2");

        Assert.Equal(ServiceError.Conflict, again.Error);
        Assert.Equal("Вече имате скала „строга“.", again.Message);
        Assert.True(other.Succeeded);
    }

    // ---------- GetForOwnerAsync / GetAsync ----------

    [Fact]
    public async Task GetForOwnerAsync_ListsTheOwnersScalesByName_WithTheirExamCounts()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeScaleService(context);
        var strict = (await service.CreateAsync(Input("Строга"), Owner)).Value!;
        await service.CreateAsync(Input("Лека", 25m, 45m, 65m, 80m), Owner);
        await service.CreateAsync(Input("Чужда"), "teacher-2");
        await UseInAnExamAsync(context, strict.Id);

        var mine = await service.GetForOwnerAsync(Owner);
        var all = await service.GetForOwnerAsync(null);

        Assert.Equal(new[] { "Лека", "Строга" }, mine.Select(s => s.Scale.Name));
        Assert.Equal(new[] { 0, 1 }, mine.Select(s => s.ExamCount));
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task GetAsync_ChecksTheRights()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeScaleService(context);
        var scale = (await service.CreateAsync(Input(), Owner)).Value!;

        Assert.True((await service.GetAsync(scale.Id, Owner, false)).Succeeded);
        Assert.True((await service.GetAsync(scale.Id, "admin", true)).Succeeded);
        Assert.Equal(ServiceError.Forbidden, (await service.GetAsync(scale.Id, "teacher-2", false)).Error);
        Assert.Equal(ServiceError.NotFound, (await service.GetAsync(999, Owner, false)).Error);
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task UpdateAsync_ChangesTheScale()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeScaleService(context);
        var scale = (await service.CreateAsync(Input(), Owner)).Value!;

        var result = await service.UpdateAsync(scale.Id, Input("По-лека", 30m, 50m, 70m, 85m), Owner, false);

        Assert.True(result.Succeeded, result.Message);
        context.ChangeTracker.Clear();
        var saved = await context.GradeScales.SingleAsync();
        Assert.Equal("По-лека", saved.Name);
        Assert.Equal((30m, 50m, 70m, 85m), (saved.Three, saved.Four, saved.Five, saved.Six));
    }

    [Fact]
    public async Task UpdateAsync_KeepingItsOwnName_IsAllowed()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeScaleService(context);
        var scale = (await service.CreateAsync(Input("Строга"), Owner)).Value!;

        Assert.True((await service.UpdateAsync(scale.Id, Input("Строга", six: 95m), Owner, false)).Succeeded);
    }

    [Fact]
    public async Task UpdateAsync_ByAnAdministrator_KeepsTheOwnerAndChecksTheOwnersNames()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeScaleService(context);
        var scale = (await service.CreateAsync(Input("Строга"), Owner)).Value!;
        await service.CreateAsync(Input("Лека", 25m, 45m, 65m, 80m), Owner);

        var taken = await service.UpdateAsync(scale.Id, Input("Лека"), "admin", true);
        var renamed = await service.UpdateAsync(scale.Id, Input("Нова"), "admin", true);

        Assert.Equal(ServiceError.Conflict, taken.Error);
        Assert.True(renamed.Succeeded);
        Assert.Equal(Owner, renamed.Value!.OwnerId);
    }

    [Fact]
    public async Task UpdateAsync_AnotherTeacher_IsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeScaleService(context);
        var scale = (await service.CreateAsync(Input(), Owner)).Value!;

        var result = await service.UpdateAsync(scale.Id, Input("Моя"), "teacher-2", false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_InvalidThresholds_LeaveTheScaleAsItWas()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeScaleService(context);
        var scale = (await service.CreateAsync(Input(), Owner)).Value!;

        var result = await service.UpdateAsync(scale.Id, Input(six: 120m), Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
        context.ChangeTracker.Clear();
        Assert.Equal(90m, (await context.GradeScales.SingleAsync()).Six);
    }

    // ---------- DeleteAsync ----------

    [Fact]
    public async Task DeleteAsync_UnusedScale_IsRemoved()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeScaleService(context);
        var scale = (await service.CreateAsync(Input(), Owner)).Value!;

        Assert.True((await service.DeleteAsync(scale.Id, Owner, false)).Succeeded);
        Assert.Empty(context.GradeScales);
    }

    [Fact]
    public async Task DeleteAsync_ScaleInUse_IsAConflict()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeScaleService(context);
        var scale = (await service.CreateAsync(Input(), Owner)).Value!;
        await UseInAnExamAsync(context, scale.Id);

        var result = await service.DeleteAsync(scale.Id, Owner, false);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal("Скалата се използва от 1 изпит. Изберете друга скала за тях и опитайте отново.", result.Message);
        Assert.Single(context.GradeScales);
    }

    [Fact]
    public async Task DeleteAsync_AnotherTeacherOrUnknown_IsRefused()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeScaleService(context);
        var scale = (await service.CreateAsync(Input(), Owner)).Value!;

        Assert.Equal(ServiceError.Forbidden, (await service.DeleteAsync(scale.Id, "teacher-2", false)).Error);
        Assert.Equal(ServiceError.NotFound, (await service.DeleteAsync(999, Owner, false)).Error);
        Assert.True((await service.DeleteAsync(scale.Id, "admin", true)).Succeeded);
    }
}
