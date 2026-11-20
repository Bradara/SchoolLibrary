using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class QuizItemServiceTests
{
    private const string Owner = "teacher-1";
    private const string Other = "teacher-2";

    // Shuffling is off here so the order assertions stay deterministic
    private static async Task<Quiz> QuizAsync(ApplicationDbContext context, string owner = Owner)
    {
        var quiz = new Quiz { Title = "Тест", OwnerId = owner, ShuffleQuestions = false, ShuffleOptions = false };
        context.Quizzes.Add(quiz);
        await context.SaveChangesAsync();
        return quiz;
    }

    private static async Task<Section> SectionWithQuestionsAsync(ApplicationDbContext context, int count, string owner = Owner)
    {
        var section = new Section { Name = "Дроби", OwnerId = owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();

        for (var i = 1; i <= count; i++)
        {
            context.Questions.Add(new Question
            {
                SectionId = section.Id,
                Text = "Въпрос " + i,
                Points = 1,
                Options = new List<AnswerOption>
                {
                    new() { Text = "верен", IsCorrect = true, Order = 1 },
                    new() { Text = "грешен", Order = 2 }
                }
            });
        }
        await context.SaveChangesAsync();
        return section;
    }

    // ---------- Adding items ----------

    [Fact]
    public async Task AddFixedQuestionAsync_PinsTheQuestion()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);
        var section = await SectionWithQuestionsAsync(context, 1);
        var questionId = context.Questions.First().Id;

        var result = await service.AddFixedQuestionAsync(quiz.Id, questionId, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(QuizItemKind.FixedQuestion, result.Value!.Kind);
        Assert.Equal(questionId, result.Value.QuestionId);
    }

    [Fact]
    public async Task AddFixedQuestionAsync_MissingQuiz_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);

        var result = await service.AddFixedQuestionAsync(999, 1, Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task AddFixedQuestionAsync_MissingQuestion_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);

        var result = await service.AddFixedQuestionAsync(quiz.Id, 999, Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task AddFixedQuestionAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);
        var section = await SectionWithQuestionsAsync(context, 1);
        var questionId = context.Questions.First().Id;

        var result = await service.AddFixedQuestionAsync(quiz.Id, questionId, Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task AddRandomSectionAsync_AddsADraw()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);
        var section = await SectionWithQuestionsAsync(context, 5);

        var result = await service.AddRandomSectionAsync(quiz.Id, section.Id, 3, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(QuizItemKind.RandomFromSection, result.Value!.Kind);
        Assert.Equal(section.Id, result.Value.SectionId);
        Assert.Equal(3, result.Value.PickCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(QuizItemService.MaxPickCount + 1)]
    public async Task AddRandomSectionAsync_WithInvalidCount_FailsAsInvalid(int count)
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);
        var section = await SectionWithQuestionsAsync(context, 5);

        var result = await service.AddRandomSectionAsync(quiz.Id, section.Id, count, Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task AddRandomSectionAsync_MissingSection_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);

        var result = await service.AddRandomSectionAsync(quiz.Id, 999, 3, Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    // ---------- AddNewQuestionAsync (into the bank root) ----------

    [Fact]
    public async Task AddNewQuestionAsync_CreatesABankQuestionInTheRootSectionAndPinsIt()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);

        var result = await service.AddNewQuestionAsync(quiz.Id, new QuestionInput("В?", new[] { "а", "б" }, 0), Owner, false);

        Assert.True(result.Succeeded);
        var section = context.Sections.Single(s => s.OwnerId == Owner && s.ParentId == null && s.Name == QuizItemService.DefaultSectionName);
        Assert.Equal(1, context.Questions.Count(q => q.SectionId == section.Id && q.QuizId == null));
        var items = await service.GetItemsAsync(quiz.Id);
        Assert.Single(items);
        Assert.Equal(QuizItemKind.FixedQuestion, items[0].Kind);
    }

    [Fact]
    public async Task AddNewQuestionAsync_ReusesTheExistingRootSection()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);

        await service.AddNewQuestionAsync(quiz.Id, new QuestionInput("В1", new[] { "а", "б" }, 0), Owner, false);
        await service.AddNewQuestionAsync(quiz.Id, new QuestionInput("В2", new[] { "а", "б" }, 0), Owner, false);

        Assert.Equal(1, context.Sections.Count(s => s.OwnerId == Owner && s.Name == QuizItemService.DefaultSectionName));
        Assert.Equal(2, (await service.GetItemsAsync(quiz.Id)).Count);
    }

    [Fact]
    public async Task AddNewQuestionAsync_Invalid_FailsAsInvalid()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);

        var result = await service.AddNewQuestionAsync(quiz.Id, new QuestionInput("В?", new[] { "само един" }, 0), Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task AddNewQuestionAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);

        var result = await service.AddNewQuestionAsync(quiz.Id, new QuestionInput("В?", new[] { "а", "б" }, 0), Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    // ---------- Order, delete, move ----------

    [Fact]
    public async Task AddItems_AreNumberedInOrder()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);
        var section = await SectionWithQuestionsAsync(context, 2);
        var questionId = context.Questions.First().Id;

        var first = (await service.AddFixedQuestionAsync(quiz.Id, questionId, Owner, false)).Value!;
        var second = (await service.AddRandomSectionAsync(quiz.Id, section.Id, 1, Owner, false)).Value!;

        Assert.Equal(1, first.Order);
        Assert.Equal(2, second.Order);
    }

    [Fact]
    public async Task DeleteItemAsync_RemovesTheItem()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);
        var section = await SectionWithQuestionsAsync(context, 1);
        var item = (await service.AddRandomSectionAsync(quiz.Id, section.Id, 1, Owner, false)).Value!;

        var result = await service.DeleteItemAsync(item.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Empty(await service.GetItemsAsync(quiz.Id));
    }

    [Fact]
    public async Task MoveItemAsync_Up_SwapsWithThePrevious()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);
        var section = await SectionWithQuestionsAsync(context, 2);
        var first = (await service.AddRandomSectionAsync(quiz.Id, section.Id, 1, Owner, false)).Value!;
        var second = (await service.AddRandomSectionAsync(quiz.Id, section.Id, 1, Owner, false)).Value!;

        await service.MoveItemAsync(second.Id, up: true, Owner, false);

        var items = await service.GetItemsAsync(quiz.Id);
        Assert.Equal(second.Id, items[0].Id);
        Assert.Equal(first.Id, items[1].Id);
    }

    // ---------- ResolveAsync ----------

    [Fact]
    public async Task ResolveAsync_Fixed_ReturnsThePinnedQuestionWithOptions()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);
        await SectionWithQuestionsAsync(context, 1);
        var questionId = context.Questions.First().Id;
        await service.AddFixedQuestionAsync(quiz.Id, questionId, Owner, false);

        var resolved = await service.ResolveAsync(quiz.Id);

        Assert.Single(resolved);
        Assert.Equal(questionId, resolved[0].Id);
        Assert.Equal(2, resolved[0].Options.Count);
    }

    [Fact]
    public async Task ResolveAsync_Random_DrawsTheRequestedCountFromTheSection()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);
        var section = await SectionWithQuestionsAsync(context, 5);
        await service.AddRandomSectionAsync(quiz.Id, section.Id, 2, Owner, false);
        var poolIds = context.Questions.Select(q => q.Id).ToHashSet();

        var resolved = await service.ResolveAsync(quiz.Id, new Random(12345));

        Assert.Equal(2, resolved.Count);
        Assert.All(resolved, q => Assert.Contains(q.Id, poolIds));
    }

    [Fact]
    public async Task ResolveAsync_Random_WhenPoolIsSmallerThanAsked_ReturnsTheWholePool()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);
        var section = await SectionWithQuestionsAsync(context, 2);
        await service.AddRandomSectionAsync(quiz.Id, section.Id, 10, Owner, false);

        var resolved = await service.ResolveAsync(quiz.Id, new Random(1));

        Assert.Equal(2, resolved.Count);
    }

    [Fact]
    public async Task ResolveAsync_IncludesDirectQuizQuestionsBeforeItems()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);

        // A question attached directly to the quiz
        context.Questions.Add(new Question
        {
            QuizId = quiz.Id,
            Text = "Пряк въпрос",
            Points = 1,
            Order = 1,
            Options = new List<AnswerOption>
            {
                new() { Text = "верен", IsCorrect = true, Order = 1 },
                new() { Text = "грешен", Order = 2 }
            }
        });
        await context.SaveChangesAsync();

        // Plus a pinned bank question
        var section = await SectionWithQuestionsAsync(context, 1);
        var bankId = context.Questions.First(q => q.SectionId != null).Id;
        await service.AddFixedQuestionAsync(quiz.Id, bankId, Owner, false);

        var resolved = await service.ResolveAsync(quiz.Id);

        Assert.Equal(2, resolved.Count);
        Assert.Equal("Пряк въпрос", resolved[0].Text);
    }

    [Fact]
    public async Task ResolveAsync_KeepsItemOrder_FixedThenRandom()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizAsync(context);
        var section = await SectionWithQuestionsAsync(context, 3);
        var pinnedId = context.Questions.First().Id;
        await service.AddFixedQuestionAsync(quiz.Id, pinnedId, Owner, false);
        await service.AddRandomSectionAsync(quiz.Id, section.Id, 1, Owner, false);

        var resolved = await service.ResolveAsync(quiz.Id, new Random(7));

        Assert.Equal(2, resolved.Count);
        Assert.Equal(pinnedId, resolved[0].Id); // the fixed item comes first
    }

    // ---------- ResolveAsync: shuffling ----------

    private static async Task<Quiz> QuizWithPinnedQuestionsAsync(ApplicationDbContext context, QuizItemService service,
        bool shuffleQuestions, bool shuffleOptions)
    {
        var quiz = await QuizAsync(context);
        quiz.ShuffleQuestions = shuffleQuestions;
        quiz.ShuffleOptions = shuffleOptions;

        var section = new Section { Name = "Банка", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();

        for (var i = 1; i <= 8; i++)
        {
            var question = new Question
            {
                SectionId = section.Id,
                Text = "Въпрос " + i,
                Points = 1,
                Order = i,
                Options = Enumerable.Range(1, 6)
                    .Select(o => new AnswerOption { Text = "Отговор " + o, IsCorrect = o == 1, Order = o })
                    .ToList()
            };
            context.Questions.Add(question);
            await context.SaveChangesAsync();
            await service.AddFixedQuestionAsync(quiz.Id, question.Id, Owner, false);
        }

        return quiz;
    }

    private static readonly List<string> AuthoredTexts = Enumerable.Range(1, 8).Select(i => "Въпрос " + i).ToList();

    [Fact]
    public async Task ResolveAsync_ShuffleOff_KeepsTheItemAndOptionOrder()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizWithPinnedQuestionsAsync(context, service, false, false);

        var resolved = await service.ResolveAsync(quiz.Id, new Random(7));

        Assert.Equal(AuthoredTexts, resolved.Select(q => q.Text));
        Assert.All(resolved, q => Assert.Equal(Enumerable.Range(1, 6), q.Options.Select(o => o.Order)));
    }

    [Fact]
    public async Task ResolveAsync_ShuffleQuestions_ReordersTheQuestionsButKeepsThemAll()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizWithPinnedQuestionsAsync(context, service, true, false);

        var resolved = await service.ResolveAsync(quiz.Id, new Random(7));

        Assert.NotEqual(AuthoredTexts, resolved.Select(q => q.Text));
        Assert.Equal(AuthoredTexts.Order(), resolved.Select(q => q.Text).Order());
        Assert.All(resolved, q => Assert.Equal(Enumerable.Range(1, 6), q.Options.Select(o => o.Order)));
    }

    [Fact]
    public async Task ResolveAsync_ShuffleOptions_ReordersTheOptionsButKeepsTheQuestionOrder()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizItemService(context);
        var quiz = await QuizWithPinnedQuestionsAsync(context, service, false, true);

        var resolved = await service.ResolveAsync(quiz.Id, new Random(7));

        Assert.Equal(AuthoredTexts, resolved.Select(q => q.Text));
        Assert.Contains(resolved, q => !q.Options.Select(o => o.Order).SequenceEqual(Enumerable.Range(1, 6)));
        Assert.All(resolved, q =>
        {
            Assert.Equal(Enumerable.Range(1, 6), q.Options.Select(o => o.Order).Order());
            Assert.Single(q.Options, o => o.IsCorrect);
        });
    }
}
