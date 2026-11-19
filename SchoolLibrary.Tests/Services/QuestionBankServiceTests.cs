using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class QuestionBankServiceTests
{
    private const string Owner = "teacher-1";
    private const string Other = "teacher-2";

    private static async Task<Section> SectionAsync(Data.ApplicationDbContext context, string? ownerId = Owner)
    {
        var section = new Section { Name = "Дроби", OwnerId = ownerId, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();
        return section;
    }

    private static QuestionInput Input(string text = "2 + 2 = ?", int correct = 0, int points = 1, params string[] options)
        => new(text, options.Length > 0 ? options : new[] { "4", "5" }, correct, points);

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_AddsBankQuestionToTheSection()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);

        var result = await service.CreateAsync(section.Id, Input(points: 2), Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(section.Id, result.Value!.SectionId);
        Assert.Null(result.Value.QuizId);
        Assert.Equal(2, result.Value.Points);
        Assert.Equal(2, result.Value.Options.Count);
    }

    [Fact]
    public async Task CreateAsync_NumbersQuestionsWithinTheSection()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);

        var first = (await service.CreateAsync(section.Id, Input(), Owner, false)).Value!;
        var second = (await service.CreateAsync(section.Id, Input(), Owner, false)).Value!;

        Assert.Equal(1, first.Order);
        Assert.Equal(2, second.Order);
    }

    [Fact]
    public async Task CreateAsync_MissingSection_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);

        var result = await service.CreateAsync(999, Input(), Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task CreateAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);

        var result = await service.CreateAsync(section.Id, Input(), Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task CreateAsync_WithOneOption_FailsAsInvalid()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);

        var result = await service.CreateAsync(section.Id, Input(options: new[] { "само един" }), Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task UpdateAsync_ReplacesTextOptionsAndPoints()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);
        var question = (await service.CreateAsync(section.Id, Input(), Owner, false)).Value!;

        var result = await service.UpdateAsync(question.Id,
            new QuestionInput("3 + 3 = ?", new[] { "5", "6", "7" }, 1, 4), Owner, false);

        Assert.True(result.Succeeded);
        var reloaded = (await service.GetQuestionAsync(question.Id))!;
        Assert.Equal("3 + 3 = ?", reloaded.Text);
        Assert.Equal(4, reloaded.Points);
        Assert.Equal(new[] { "5", "6", "7" }, reloaded.Options.OrderBy(o => o.Order).Select(o => o.Text));
        Assert.Equal("6", reloaded.Options.Single(o => o.IsCorrect).Text);
    }

    [Fact]
    public async Task UpdateAsync_Missing_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);

        var result = await service.UpdateAsync(999, Input(), Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);
        var question = (await service.CreateAsync(section.Id, Input(), Owner, false)).Value!;

        var result = await service.UpdateAsync(question.Id, Input(text: "Друг"), Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    // ---------- DeleteAsync ----------

    [Fact]
    public async Task DeleteAsync_RemovesTheQuestion()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);
        var question = (await service.CreateAsync(section.Id, Input(), Owner, false)).Value!;

        var result = await service.DeleteAsync(question.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Null(await service.GetQuestionAsync(question.Id));
    }

    [Fact]
    public async Task DeleteAsync_Missing_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);

        var result = await service.DeleteAsync(999, Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task DeleteAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);
        var question = (await service.CreateAsync(section.Id, Input(), Owner, false)).Value!;

        var result = await service.DeleteAsync(question.Id, Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    // ---------- GetBySectionAsync ----------

    [Fact]
    public async Task GetBySectionAsync_ReturnsTheSectionsQuestionsOrderedWithOptions()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);
        await service.CreateAsync(section.Id, Input(text: "Първи"), Owner, false);
        await service.CreateAsync(section.Id, Input(text: "Втори"), Owner, false);

        var questions = await service.GetBySectionAsync(section.Id);

        Assert.Equal(new[] { "Първи", "Втори" }, questions.Select(q => q.Text));
        Assert.All(questions, q => Assert.Equal(2, q.Options.Count));
    }

    [Fact]
    public async Task GetQuestionAsync_Missing_ReturnsNull()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);

        Assert.Null(await service.GetQuestionAsync(1));
    }

    // ---------- ImportAikenAsync ----------

    [Fact]
    public async Task ImportAikenAsync_AddsTheValidQuestionsToTheSection()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);

        var text = "Въпрос 1\nA. а\nB. б\nANSWER: A\n\nВъпрос 2\nA. в\nB. г\nANSWER: B";
        var result = await service.ImportAikenAsync(section.Id, text, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Added);
        Assert.Empty(result.Value.Errors);
        Assert.Equal(2, (await service.GetBySectionAsync(section.Id)).Count);
    }

    [Fact]
    public async Task ImportAikenAsync_AddsGoodOnesAndReportsTheBad()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);

        var text = "Добър\nA. а\nB. б\nANSWER: A\n\nЛош без отговор\nA. а\nB. б";
        var result = await service.ImportAikenAsync(section.Id, text, Owner, false);

        Assert.Equal(1, result.Value!.Added);
        Assert.Single(result.Value.Errors);
    }

    [Fact]
    public async Task ImportAikenAsync_MissingSection_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);

        var result = await service.ImportAikenAsync(999, "x", Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task ImportAikenAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);

        var result = await service.ImportAikenAsync(section.Id, "В?\nA. а\nB. б\nANSWER: A", Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }
}
