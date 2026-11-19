using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class SectionServiceTests
{
    private const string Owner = "teacher-1";
    private const string Other = "teacher-2";

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_SavesTrimmedAndOwned()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);

        var result = await service.CreateAsync(new SectionInput("  Дроби  ", "  тема  "), Owner);

        Assert.True(result.Succeeded);
        Assert.Equal("Дроби", result.Value!.Name);
        Assert.Equal("тема", result.Value.Description);
        Assert.Equal(Owner, result.Value.OwnerId);
    }

    [Fact]
    public async Task CreateAsync_EmptyName_FailsAsInvalid()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);

        var result = await service.CreateAsync(new SectionInput("   ", null), Owner);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task CreateAsync_TooLongName_FailsAsInvalid()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);

        var result = await service.CreateAsync(new SectionInput(new string('x', 201), null), Owner);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task UpdateAsync_ChangesNameAndDescription()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);
        var section = (await service.CreateAsync(new SectionInput("Старо", null), Owner)).Value!;

        var result = await service.UpdateAsync(section.Id, new SectionInput("Ново", "описание"), Owner, false);

        Assert.True(result.Succeeded);
        var reloaded = (await service.GetByIdAsync(section.Id))!;
        Assert.Equal("Ново", reloaded.Name);
        Assert.Equal("описание", reloaded.Description);
    }

    [Fact]
    public async Task UpdateAsync_Missing_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);

        var result = await service.UpdateAsync(999, new SectionInput("Х", null), Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);
        var section = (await service.CreateAsync(new SectionInput("Мой", null), Owner)).Value!;

        var result = await service.UpdateAsync(section.Id, new SectionInput("Чужд", null), Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_ByAdministrator_IsAllowed()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);
        var section = (await service.CreateAsync(new SectionInput("Мой", null), Owner)).Value!;

        var result = await service.UpdateAsync(section.Id, new SectionInput("Админ", null), Other, isAdministrator: true);

        Assert.True(result.Succeeded);
    }

    // ---------- DeleteAsync ----------

    [Fact]
    public async Task DeleteAsync_RemovesAnEmptySection()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);
        var section = (await service.CreateAsync(new SectionInput("За триене", null), Owner)).Value!;

        var result = await service.DeleteAsync(section.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Null(await service.GetByIdAsync(section.Id));
    }

    [Fact]
    public async Task DeleteAsync_WithQuestions_FailsAsConflict()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);
        var section = (await service.CreateAsync(new SectionInput("Пълен", null), Owner)).Value!;

        var quiz = new Quiz { Title = "Т", OwnerId = Owner };
        context.Quizzes.Add(quiz);
        await context.SaveChangesAsync();
        context.Questions.Add(new Question { QuizId = quiz.Id, SectionId = section.Id, Text = "В", Points = 1 });
        await context.SaveChangesAsync();

        var result = await service.DeleteAsync(section.Id, Owner, false);

        Assert.Equal(ServiceError.Conflict, result.Error);
    }

    [Fact]
    public async Task DeleteAsync_Missing_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);

        var result = await service.DeleteAsync(999, Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task DeleteAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);
        var section = (await service.CreateAsync(new SectionInput("Мой", null), Owner)).Value!;

        var result = await service.DeleteAsync(section.Id, Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    // ---------- GetManagedAsync ----------

    [Fact]
    public async Task GetManagedAsync_WithOwner_ReturnsOnlyTheirSectionsWithCounts()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);
        var mine = (await service.CreateAsync(new SectionInput("Мой", null), Owner)).Value!;
        await service.CreateAsync(new SectionInput("Чужд", null), Other);

        var quiz = new Quiz { Title = "Т", OwnerId = Owner };
        context.Quizzes.Add(quiz);
        await context.SaveChangesAsync();
        context.Questions.Add(new Question { QuizId = quiz.Id, SectionId = mine.Id, Text = "В1", Points = 1 });
        context.Questions.Add(new Question { QuizId = quiz.Id, SectionId = mine.Id, Text = "В2", Points = 1 });
        await context.SaveChangesAsync();

        var page = await service.GetManagedAsync(Owner, page: 1);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Мой", page.Items[0].Section.Name);
        Assert.Equal(2, page.Items[0].QuestionCount);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SectionService(context);

        Assert.Null(await service.GetByIdAsync(1));
    }

    // ---------- SectionPermissions ----------

    [Fact]
    public void CanManage_AllowsOwnerAndAdministrator_ButNotOthers()
    {
        var section = new Section { OwnerId = Owner };

        Assert.True(SectionPermissions.CanManage(section, Owner, false));
        Assert.True(SectionPermissions.CanManage(section, Other, true));
        Assert.False(SectionPermissions.CanManage(section, Other, false));
    }

    [Fact]
    public void CanManage_WithNoOwner_AllowsOnlyAdministrator()
    {
        var section = new Section { OwnerId = null };

        Assert.True(SectionPermissions.CanManage(section, "anyone", true));
        Assert.False(SectionPermissions.CanManage(section, "anyone", false));
    }
}
