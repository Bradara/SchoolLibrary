using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.Tests.Web;

namespace SchoolLibrary.Tests.Services;

/// <summary>
/// Deleting bank questions: a question pinned to a quiz is taken out of it, an answered question is kept
/// (the database restricts both, which the in-memory provider does not check), and several can be deleted at once.
/// </summary>
public class QuestionDeleteTests
{
    private const string Owner = "teacher-1";
    private const string Other = "teacher-2";

    private static async Task<Section> SectionAsync(Data.ApplicationDbContext context, string ownerId = Owner)
    {
        var section = new Section { Name = "Дроби", OwnerId = ownerId, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();
        return section;
    }

    private static async Task<Question> QuestionAsync(QuestionBankService service, Section section, string text = "2 + 2 = ?")
        => (await service.CreateAsync(section.Id, new QuestionInput(text, new[] { "4", "5" }, 0), section.OwnerId!, false)).Value!;

    private static async Task<QuizItem> PinAsync(Data.ApplicationDbContext context, Question question)
    {
        var quiz = new Quiz { Title = "Тест", OwnerId = Owner };
        context.Quizzes.Add(quiz);
        await context.SaveChangesAsync();
        var item = new QuizItem { QuizId = quiz.Id, Order = 1, Kind = QuizItemKind.FixedQuestion, QuestionId = question.Id };
        context.QuizItems.Add(item);
        await context.SaveChangesAsync();
        return item;
    }

    private static async Task SolveAsync(Data.ApplicationDbContext context, Question question, bool servedOnly = false)
    {
        var quiz = new Quiz { Title = "Изпит", OwnerId = Owner };
        context.Quizzes.Add(quiz);
        await context.SaveChangesAsync();
        var attempt = new QuizAttempt { QuizId = quiz.Id, Token = Guid.NewGuid(), StudentName = "Ученик", StartedOn = DateTime.UtcNow };
        attempt.Questions.Add(new AttemptQuestion { QuestionId = question.Id, Order = 1 });
        if (!servedOnly)
        {
            attempt.Answers.Add(new AttemptAnswer { QuestionId = question.Id });
        }

        context.QuizAttempts.Add(attempt);
        await context.SaveChangesAsync();
    }

    // ---------- One question ----------

    [Fact]
    public async Task Delete_APinnedQuestion_TakesItOutOfTheQuiz()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var question = await QuestionAsync(service, await SectionAsync(context));
        var pin = await PinAsync(context, question);

        var result = await service.DeleteAsync(question.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.False(await context.QuizItems.AnyAsync(i => i.Id == pin.Id));
        Assert.True(await context.Quizzes.AnyAsync(q => q.Id == pin.QuizId));
        Assert.Null(await service.GetQuestionAsync(question.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_ASolvedQuestion_FailsAsConflict_AndKeepsIt(bool servedOnly)
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var question = await QuestionAsync(service, await SectionAsync(context));
        await SolveAsync(context, question, servedOnly);

        var result = await service.DeleteAsync(question.Id, Owner, false);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.NotNull(result.Message);
        Assert.NotNull(await service.GetQuestionAsync(question.Id));
    }

    // ---------- Several questions ----------

    [Fact]
    public async Task DeleteMany_DeletesThePickedQuestions_AndTheirPins()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);
        var first = await QuestionAsync(service, section, "1");
        var second = await QuestionAsync(service, section, "2");
        var kept = await QuestionAsync(service, section, "3");
        await PinAsync(context, second);

        var result = await service.DeleteQuestionsAsync(new[] { first.Id, second.Id, first.Id }, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(new QuestionDeleteSummary(2, 0), result.Value);
        Assert.Equal(new[] { kept.Id }, (await service.GetBySectionAsync(section.Id)).Select(q => q.Id));
        Assert.False(await context.QuizItems.AnyAsync());
        Assert.False(await context.AnswerOptions.AnyAsync(o => o.QuestionId == first.Id || o.QuestionId == second.Id));
    }

    [Fact]
    public async Task DeleteMany_KeepsSolvedQuestions_AndCountsThem()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = await SectionAsync(context);
        var free = await QuestionAsync(service, section, "1");
        var solved = await QuestionAsync(service, section, "2");
        await SolveAsync(context, solved);

        var result = await service.DeleteQuestionsAsync(new[] { free.Id, solved.Id }, Owner, false);

        Assert.Equal(new QuestionDeleteSummary(1, 1), result.Value);
        Assert.Null(await service.GetQuestionAsync(free.Id));
        Assert.NotNull(await service.GetQuestionAsync(solved.Id));
    }

    [Fact]
    public async Task DeleteMany_SkipsMissingAndSomeoneElsesQuestions()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var foreign = await QuestionAsync(service, await SectionAsync(context, Other));

        var result = await service.DeleteQuestionsAsync(new[] { foreign.Id, 999 }, Owner, false);

        Assert.Equal(new QuestionDeleteSummary(0, 0), result.Value);
        Assert.NotNull(await service.GetQuestionAsync(foreign.Id));
    }

    [Fact]
    public async Task DeleteMany_ByAnAdministrator_DeletesAnyonesQuestions()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var foreign = await QuestionAsync(service, await SectionAsync(context, Other));

        var result = await service.DeleteQuestionsAsync(new[] { foreign.Id }, "admin", true);

        Assert.Equal(new QuestionDeleteSummary(1, 0), result.Value);
    }

    [Fact]
    public async Task DeleteMany_Nothing_DeletesNothing()
    {
        using var context = TestDbContextFactory.Create();

        var result = await new QuestionBankService(context).DeleteQuestionsAsync(Array.Empty<int>(), Owner, false);

        Assert.Equal(new QuestionDeleteSummary(0, 0), result.Value);
    }

    // ---------- The controller ----------

    private static SectionsController Controller(Mock<IQuestionBankService> questions)
        => new SectionsController(Mock.Of<ISectionService>(), questions.Object).WithUser("t1", Roles.Teacher);

    [Fact]
    public async Task DeleteQuestion_Solved_ShowsTheReason()
    {
        var questions = new Mock<IQuestionBankService>();
        questions.Setup(q => q.DeleteAsync(9, "t1", false))
            .ReturnsAsync(ServiceResult<Question>.Failure(ServiceError.Conflict, "Решаван"));
        var controller = Controller(questions);

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.DeleteQuestion(5, 9));

        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("Решаван", controller.TempData["ErrorMessage"]);
        Assert.False(controller.TempData.ContainsKey("SuccessMessage"));
    }

    [Fact]
    public async Task DeleteQuestions_ReportsDeletedAndKept()
    {
        var questions = new Mock<IQuestionBankService>();
        questions.Setup(q => q.DeleteQuestionsAsync(It.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 1, 2, 3 })), "t1", false))
            .ReturnsAsync(ServiceResult<QuestionDeleteSummary>.Success(new QuestionDeleteSummary(2, 1)));
        var controller = Controller(questions);

        var redirect = Assert.IsType<RedirectToActionResult>(await controller.DeleteQuestions(5, new[] { 1, 2, 3 }));

        Assert.Equal(5, redirect.RouteValues!["id"]);
        Assert.Equal("Изтрити въпроси: 2.", controller.TempData["SuccessMessage"]);
        Assert.Contains("Не са изтрити 1", (string)controller.TempData["ErrorMessage"]!);
    }

    [Fact]
    public async Task DeleteQuestions_NothingPicked_AsksToPick()
    {
        var questions = new Mock<IQuestionBankService>();
        var controller = Controller(questions);

        Assert.IsType<RedirectToActionResult>(await controller.DeleteQuestions(5, Array.Empty<int>()));

        Assert.Equal("Изберете поне един въпрос.", controller.TempData["ErrorMessage"]);
        questions.Verify(q => q.DeleteQuestionsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }
}
