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

    // ---------- MoveQuestionsAsync ----------

    [Fact]
    public async Task MoveQuestionsAsync_MovesCheckedQuestionsAndAppendsThem()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var from = await SectionAsync(context);
        var to = new Section { Name = "Геометрия", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(to);
        await context.SaveChangesAsync();

        var q1 = (await service.CreateAsync(from.Id, Input(text: "Първи"), Owner, false)).Value!;
        var q2 = (await service.CreateAsync(from.Id, Input(text: "Втори"), Owner, false)).Value!;
        var existing = (await service.CreateAsync(to.Id, Input(text: "Вече тук"), Owner, false)).Value!;

        var result = await service.MoveQuestionsAsync(new[] { q1.Id, q2.Id }, to.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value);
        Assert.Empty(await service.GetBySectionAsync(from.Id));
        var moved = await service.GetBySectionAsync(to.Id);
        // Existing question keeps its place; the moved ones are appended after it, in order
        Assert.Equal(new[] { "Вече тук", "Първи", "Втори" }, moved.Select(q => q.Text));
    }

    [Fact]
    public async Task MoveQuestionsAsync_SkipsQuestionsFromAnotherTeachersSection()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var mine = await SectionAsync(context);
        var theirs = new Section { Name = "Чужд", OwnerId = Other, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(theirs);
        await context.SaveChangesAsync();

        var ownQuestion = (await service.CreateAsync(mine.Id, Input(text: "Мой"), Owner, false)).Value!;
        var foreignQuestion = (await service.CreateAsync(theirs.Id, Input(text: "Чужд"), Other, false)).Value!;

        var result = await service.MoveQuestionsAsync(new[] { ownQuestion.Id, foreignQuestion.Id }, mine.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value); // own question is already in the target; the foreign one is skipped
        Assert.Equal(theirs.Id, (await service.GetQuestionAsync(foreignQuestion.Id))!.SectionId);
    }

    [Fact]
    public async Task MoveQuestionsAsync_MissingTarget_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);

        var result = await service.MoveQuestionsAsync(new[] { 1 }, 999, Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task MoveQuestionsAsync_TargetOfAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var target = await SectionAsync(context);

        var result = await service.MoveQuestionsAsync(new[] { 1 }, target.Id, Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task MoveQuestionsAsync_AdministratorMovesAcrossOwners()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var from = await SectionAsync(context, Other);
        var to = new Section { Name = "Админ цел", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(to);
        await context.SaveChangesAsync();
        var question = (await service.CreateAsync(from.Id, Input(), Other, false)).Value!;

        var result = await service.MoveQuestionsAsync(new[] { question.Id }, to.Id, "admin", true);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value);
        Assert.Equal(to.Id, (await service.GetQuestionAsync(question.Id))!.SectionId);
    }

    // ---------- ImportAsync (Aiken + Moodle XML) ----------

    [Fact]
    public async Task ImportAsync_AddsTheValidQuestionsToTheSection()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);

        var text = "Въпрос 1\nA. а\nB. б\nANSWER: A\n\nВъпрос 2\nA. в\nB. г\nANSWER: B";
        var result = await service.ImportAsync(section.Id, text, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Added);
        Assert.Empty(result.Value.Errors);
        Assert.Equal(2, (await service.GetBySectionAsync(section.Id)).Count);
    }

    [Fact]
    public async Task ImportAsync_AddsGoodOnesAndReportsTheBad()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);

        var text = "Добър\nA. а\nB. б\nANSWER: A\n\nЛош без отговор\nA. а\nB. б";
        var result = await service.ImportAsync(section.Id, text, Owner, false);

        Assert.Equal(1, result.Value!.Added);
        Assert.Single(result.Value.Errors);
    }

    [Fact]
    public async Task ImportAsync_MissingSection_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);

        var result = await service.ImportAsync(999, "x", Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task ImportAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);

        var result = await service.ImportAsync(section.Id, "В?\nA. а\nB. б\nANSWER: A", Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task ImportAsync_DetectsMoodleXmlAndAddsTheQuestion()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);

        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"multichoice\"><single>true</single>" +
                  "<questiontext><text>Колко е 2 + 2?</text></questiontext>" +
                  "<answer fraction=\"0\"><text>3</text></answer>" +
                  "<answer fraction=\"100\"><text>4</text></answer></question></quiz>";
        var result = await service.ImportAsync(section.Id, xml, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.Added);
        var question = Assert.Single(await service.GetBySectionAsync(section.Id));
        Assert.Equal("Колко е 2 + 2?", question.Text);
        Assert.Equal("4", question.Options.Single(o => o.IsCorrect).Text);
    }
}
