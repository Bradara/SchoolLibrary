using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Exams;

namespace SchoolLibrary.Tests.Services;

public class SchoolClassServiceTests
{
    private const string Owner = "teacher-1";
    private const string Other = "teacher-2";

    [Fact]
    public async Task CreateAsync_TrimsTheNameAndStoresTheOwner()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SchoolClassService(context);

        var result = await service.CreateAsync("  5   А  ", Owner);

        Assert.True(result.Succeeded);
        Assert.Equal("5 А", result.Value!.Name);
        Assert.Equal(Owner, result.Value.OwnerId);
        Assert.Equal(DateTimeKind.Utc, result.Value.CreatedOn.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_EmptyName_IsInvalid(string name)
    {
        using var context = TestDbContextFactory.Create();

        var result = await new SchoolClassService(context).CreateAsync(name, Owner);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task CreateAsync_TooLongName_IsInvalid()
    {
        using var context = TestDbContextFactory.Create();

        var result = await new SchoolClassService(context).CreateAsync(new string('а', SchoolClassService.MaxNameLength + 1), Owner);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task CreateAsync_SameNameForTheSameTeacher_IgnoringCase_IsAConflict()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SchoolClassService(context);
        await service.CreateAsync("5а", Owner);

        var result = await service.CreateAsync("5А", Owner);

        Assert.Equal(ServiceError.Conflict, result.Error);
    }

    [Fact]
    public async Task CreateAsync_SameNameForAnotherTeacher_IsAllowed()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SchoolClassService(context);
        await service.CreateAsync("5А", Owner);

        var result = await service.CreateAsync("5А", Other);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task GetForOwnerAsync_ReturnsOnlyTheOwnersClassesByNameWithExamCounts()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SchoolClassService(context);
        var b = (await service.CreateAsync("6Б", Owner)).Value!;
        await service.CreateAsync("5А", Owner);
        await service.CreateAsync("7В", Other);
        var quiz = new Quiz { Title = "Т", OwnerId = Owner };
        context.Quizzes.Add(quiz);
        await context.SaveChangesAsync();
        context.ExamAssignments.Add(new ExamAssignment { QuizId = quiz.Id, SchoolClassId = b.Id, Code = "AAAAAA" });
        await context.SaveChangesAsync();

        var mine = await service.GetForOwnerAsync(Owner);
        var all = await service.GetForOwnerAsync(null);

        Assert.Equal(new[] { "5А", "6Б" }, mine.Select(c => c.SchoolClass.Name));
        Assert.Equal(new[] { 0, 1 }, mine.Select(c => c.ExamCount));
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task GetByIdAsync_MissingReturnsNull()
    {
        using var context = TestDbContextFactory.Create();

        Assert.Null(await new SchoolClassService(context).GetByIdAsync(99));
    }

    [Fact]
    public async Task RenameAsync_ByOwner_ChangesTheName()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SchoolClassService(context);
        var created = (await service.CreateAsync("5А", Owner)).Value!;

        var result = await service.RenameAsync(created.Id, " 5Б ", Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal("5Б", (await service.GetByIdAsync(created.Id))!.Name);
    }

    [Fact]
    public async Task RenameAsync_ToItsOwnNameInAnotherCase_IsAllowed()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SchoolClassService(context);
        var created = (await service.CreateAsync("5а", Owner)).Value!;

        var result = await service.RenameAsync(created.Id, "5А", Owner, false);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task RenameAsync_ToAnotherClassName_IsAConflict()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SchoolClassService(context);
        await service.CreateAsync("5А", Owner);
        var b = (await service.CreateAsync("5Б", Owner)).Value!;

        var result = await service.RenameAsync(b.Id, "5А", Owner, false);

        Assert.Equal(ServiceError.Conflict, result.Error);
    }

    [Fact]
    public async Task RenameAsync_AnotherTeachersClass_IsForbidden_ButAnAdministratorMay()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SchoolClassService(context);
        var created = (await service.CreateAsync("5А", Owner)).Value!;

        Assert.Equal(ServiceError.Forbidden, (await service.RenameAsync(created.Id, "5Б", Other, false)).Error);
        Assert.True((await service.RenameAsync(created.Id, "5Б", Other, true)).Succeeded);
    }

    [Fact]
    public async Task RenameAsync_Missing_IsNotFound()
    {
        using var context = TestDbContextFactory.Create();

        var result = await new SchoolClassService(context).RenameAsync(99, "5А", Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task RenameAsync_EmptyName_IsInvalid()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SchoolClassService(context);
        var created = (await service.CreateAsync("5А", Owner)).Value!;

        Assert.Equal(ServiceError.Invalid, (await service.RenameAsync(created.Id, " ", Owner, false)).Error);
    }

    [Fact]
    public async Task DeleteAsync_ByOwner_RemovesTheClass()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SchoolClassService(context);
        var created = (await service.CreateAsync("5А", Owner)).Value!;

        var result = await service.DeleteAsync(created.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Null(await service.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task DeleteAsync_AnotherTeachersClass_IsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new SchoolClassService(context);
        var created = (await service.CreateAsync("5А", Owner)).Value!;

        Assert.Equal(ServiceError.Forbidden, (await service.DeleteAsync(created.Id, Other, false)).Error);
        Assert.NotNull(await service.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task DeleteAsync_Missing_IsNotFound()
    {
        using var context = TestDbContextFactory.Create();

        Assert.Equal(ServiceError.NotFound, (await new SchoolClassService(context).DeleteAsync(99, Owner, false)).Error);
    }
}
