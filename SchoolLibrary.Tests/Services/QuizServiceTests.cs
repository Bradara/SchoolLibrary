using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class QuizServiceTests
{
    private const string Owner = "teacher-1";
    private const string Other = "teacher-2";
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private static QuizInput Practice(string title = "Упражнение", int? resourceId = null)
        => new(title, null, QuizMode.Practice, null, resourceId);

    private static QuizInput Exam(string title = "Изпит", int? minutes = 30)
        => new(title, null, QuizMode.Exam, minutes, null);

    private static QuestionInput Question(string text = "2 + 2 = ?", int correct = 0, params string[] options)
        => new(text, options.Length > 0 ? options : new[] { "4", "5" }, correct);

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_Practice_SavesUnpublishedWithNoCodeAndTrimmedTitle()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.CreateAsync(new QuizInput("  Дроби  ", "  описание  ", QuizMode.Practice, null, null), Owner);

        Assert.True(result.Succeeded);
        var quiz = result.Value!;
        Assert.Equal("Дроби", quiz.Title);
        Assert.Equal("описание", quiz.Description);
        Assert.Null(quiz.Code);
        Assert.Null(quiz.TimeLimitMinutes);
        Assert.False(quiz.IsPublished);
        Assert.Equal(Owner, quiz.OwnerId);
        Assert.NotEqual(default, quiz.CreatedOn);
        Assert.Equal(DateTimeKind.Utc, quiz.CreatedOn.Kind);
    }

    [Fact]
    public async Task CreateAsync_Exam_GeneratesAReadableCodeAndKeepsTheTimeLimit()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.CreateAsync(Exam(minutes: 45), Owner);

        Assert.True(result.Succeeded);
        var quiz = result.Value!;
        Assert.NotNull(quiz.Code);
        Assert.Equal(QuizService.CodeLength, quiz.Code!.Length);
        Assert.All(quiz.Code, c => Assert.Contains(c, Alphabet));
        Assert.Equal(45, quiz.TimeLimitMinutes);
    }

    [Fact]
    public async Task CreateAsync_Practice_IgnoresATimeLimit()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.CreateAsync(new QuizInput("Тест", null, QuizMode.Practice, 30, null), Owner);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.TimeLimitMinutes);
    }

    [Fact]
    public async Task CreateAsync_GivesEveryExamADifferentCode()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var a = await service.CreateAsync(Exam("A"), Owner);
        var b = await service.CreateAsync(Exam("B"), Owner);

        Assert.NotEqual(a.Value!.Code, b.Value!.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_WithoutTitle_FailsAsInvalid(string title)
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.CreateAsync(Practice(title), Owner);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task CreateAsync_WithTooLongTitle_FailsAsInvalid()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.CreateAsync(Practice(new string('x', QuizService.MaxTitleLength + 1)), Owner);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(QuizService.MaxTimeLimitMinutes + 1)]
    public async Task CreateAsync_ExamWithTimeOutOfRange_FailsAsInvalid(int minutes)
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.CreateAsync(Exam(minutes: minutes), Owner);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task CreateAsync_ExamWithoutTimeLimit_IsAllowed()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.CreateAsync(Exam(minutes: null), Owner);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.TimeLimitMinutes);
    }

    [Fact]
    public async Task CreateAsync_WithMissingResource_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.CreateAsync(Practice(resourceId: 999), Owner);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task CreateAsync_WithExistingResource_LinksIt()
    {
        using var context = TestDbContextFactory.Create();
        var resource = new Resource { Title = "Урок", Author = "x", Url = "https://e.com/1" };
        context.Resources.Add(resource);
        await context.SaveChangesAsync();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.CreateAsync(Practice(resourceId: resource.Id), Owner);

        Assert.True(result.Succeeded);
        Assert.Equal(resource.Id, result.Value!.ResourceId);
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task UpdateAsync_ChangesTheDetails()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice("Старо"), Owner)).Value!;

        var result = await service.UpdateAsync(quiz.Id, Practice("Ново"), Owner, isAdministrator: false);

        Assert.True(result.Succeeded);
        Assert.Equal("Ново", (await service.GetByIdAsync(quiz.Id))!.Title);
    }

    [Fact]
    public async Task UpdateAsync_ExamToPractice_ClearsCodeAndTime()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Exam(minutes: 20), Owner)).Value!;

        var result = await service.UpdateAsync(quiz.Id, Practice("Вече упражнение"), Owner, isAdministrator: false);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.Code);
        Assert.Null(result.Value!.TimeLimitMinutes);
    }

    [Fact]
    public async Task UpdateAsync_PracticeToExam_GeneratesACode()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.UpdateAsync(quiz.Id, Exam(minutes: 15), Owner, isAdministrator: false);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value!.Code);
        Assert.Equal(15, result.Value!.TimeLimitMinutes);
    }

    [Fact]
    public async Task UpdateAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.UpdateAsync(quiz.Id, Practice("Опит"), Other, isAdministrator: false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_Missing_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.UpdateAsync(999, Practice(), Owner, isAdministrator: false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_ChangingModeAfterAnAttempt_FailsAsConflict()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Exam(), Owner)).Value!;
        context.QuizAttempts.Add(new QuizAttempt { QuizId = quiz.Id, StudentName = "Иван", StartedOn = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var result = await service.UpdateAsync(quiz.Id, Practice("Смяна"), Owner, isAdministrator: false);

        Assert.Equal(ServiceError.Conflict, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_SameModeAfterAnAttempt_IsAllowed()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Exam(minutes: 30), Owner)).Value!;
        context.QuizAttempts.Add(new QuizAttempt { QuizId = quiz.Id, StudentName = "Иван", StartedOn = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var result = await service.UpdateAsync(quiz.Id, Exam("Ново заглавие", minutes: 40), Owner, isAdministrator: false);

        Assert.True(result.Succeeded);
        Assert.Equal(40, result.Value!.TimeLimitMinutes);
    }

    // ---------- DeleteAsync ----------

    [Fact]
    public async Task DeleteAsync_ByOwner_RemovesTheQuiz()
    {
        // The database cascades the quiz's questions, options and attempts (see the migration);
        // in-memory does not enforce that, so this only checks the quiz itself is gone.
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id, Question(), Owner, isAdministrator: false);

        var result = await service.DeleteAsync(quiz.Id, Owner, isAdministrator: false);

        Assert.True(result.Succeeded);
        Assert.Null(await service.GetByIdAsync(quiz.Id));
    }

    [Fact]
    public async Task DeleteAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.DeleteAsync(quiz.Id, Other, isAdministrator: false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task DeleteAsync_ByAdministrator_Succeeds()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.DeleteAsync(quiz.Id, Other, isAdministrator: true);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DeleteAsync_Missing_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.DeleteAsync(123, Owner, isAdministrator: false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    // ---------- SetPublishedAsync ----------

    [Fact]
    public async Task SetPublishedAsync_WithoutQuestions_FailsAsInvalid()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.SetPublishedAsync(quiz.Id, true, Owner, isAdministrator: false);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.False((await service.GetByIdAsync(quiz.Id))!.IsPublished);
    }

    [Fact]
    public async Task SetPublishedAsync_WithAQuestion_PublishesAndUnpublishes()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id, Question(), Owner, isAdministrator: false);

        Assert.True((await service.SetPublishedAsync(quiz.Id, true, Owner, false)).Succeeded);
        Assert.True((await service.GetByIdAsync(quiz.Id))!.IsPublished);

        Assert.True((await service.SetPublishedAsync(quiz.Id, false, Owner, false)).Succeeded);
        Assert.False((await service.GetByIdAsync(quiz.Id))!.IsPublished);
    }

    [Fact]
    public async Task SetPublishedAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.SetPublishedAsync(quiz.Id, true, Other, isAdministrator: false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    // ---------- AddQuestionAsync ----------

    [Fact]
    public async Task AddQuestionAsync_StoresOptionsInOrderAndMarksTheCorrectOne()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.AddQuestionAsync(quiz.Id, Question("Столица?", 2, "София", "Пловдив", "Варна"), Owner, false);

        Assert.True(result.Succeeded);
        var saved = (await service.GetByIdAsync(quiz.Id))!.Questions.Single();
        Assert.Equal(new[] { "София", "Пловдив", "Варна" }, saved.Options.Select(o => o.Text));
        Assert.Equal(new[] { false, false, true }, saved.Options.Select(o => o.IsCorrect));
        Assert.Equal(QuestionType.SingleChoice, saved.Type);
    }

    [Fact]
    public async Task AddQuestionAsync_NumbersQuestionsInOrder()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        await service.AddQuestionAsync(quiz.Id, Question("Първи"), Owner, false);
        await service.AddQuestionAsync(quiz.Id, Question("Втори"), Owner, false);

        var questions = (await service.GetByIdAsync(quiz.Id))!.Questions.ToList();
        Assert.Equal(new[] { "Първи", "Втори" }, questions.Select(q => q.Text));
        Assert.Equal(new[] { 1, 2 }, questions.Select(q => q.Order));
    }

    [Theory]
    [InlineData(1)]   // fewer than two options
    [InlineData(11)]  // more than the maximum
    public async Task AddQuestionAsync_WithWrongNumberOfOptions_FailsAsInvalid(int optionCount)
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;
        var options = Enumerable.Range(1, optionCount).Select(i => "опция " + i).ToArray();

        var result = await service.AddQuestionAsync(quiz.Id, Question("В?", 0, options), Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task AddQuestionAsync_WithAnEmptyOption_FailsAsInvalid()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.AddQuestionAsync(quiz.Id, Question("В?", 0, "ок", "   "), Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task AddQuestionAsync_WithCorrectIndexOutOfRange_FailsAsInvalid(int correct)
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.AddQuestionAsync(quiz.Id, Question("В?", correct, "а", "б"), Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task AddQuestionAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.AddQuestionAsync(quiz.Id, Question(), Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task AddQuestionAsync_ToMissingQuiz_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.AddQuestionAsync(999, Question(), Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task AddQuestionAsync_WhenTheQuizHasAttempts_FailsAsConflict()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Exam(), Owner)).Value!;
        context.QuizAttempts.Add(new QuizAttempt { QuizId = quiz.Id, StudentName = "Иван", StartedOn = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var result = await service.AddQuestionAsync(quiz.Id, Question(), Owner, false);

        Assert.Equal(ServiceError.Conflict, result.Error);
    }

    [Fact]
    public async Task AddQuestionAsync_StoresThePoints()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.AddQuestionAsync(quiz.Id,
            new QuestionInput("В?", new[] { "а", "б" }, 0, Points: 3), Owner, false);

        Assert.Equal(3, result.Value!.Points);
    }

    [Fact]
    public async Task AddQuestionAsync_DefaultsToOnePoint()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.AddQuestionAsync(quiz.Id, Question(), Owner, false);

        Assert.Equal(1, result.Value!.Points);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(QuizService.MaxQuestionPoints + 1)]
    public async Task AddQuestionAsync_WithPointsOutOfRange_FailsAsInvalid(int points)
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;

        var result = await service.AddQuestionAsync(quiz.Id,
            new QuestionInput("В?", new[] { "а", "б" }, 0, points), Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task UpdateQuestionAsync_ChangesThePoints()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;
        var question = (await service.AddQuestionAsync(quiz.Id, Question(), Owner, false)).Value!;

        await service.UpdateQuestionAsync(question.Id,
            new QuestionInput("В?", new[] { "а", "б" }, 0, Points: 5), Owner, false);

        Assert.Equal(5, (await service.GetQuestionAsync(question.Id))!.Points);
    }

    // ---------- DeleteQuestionAsync ----------

    [Fact]
    public async Task DeleteQuestionAsync_RemovesTheQuestion()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;
        var question = (await service.AddQuestionAsync(quiz.Id, Question(), Owner, false)).Value!;

        var result = await service.DeleteQuestionAsync(question.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Empty((await service.GetByIdAsync(quiz.Id))!.Questions);
    }

    [Fact]
    public async Task DeleteQuestionAsync_Missing_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.DeleteQuestionAsync(999, Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task DeleteQuestionAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;
        var question = (await service.AddQuestionAsync(quiz.Id, Question(), Owner, false)).Value!;

        var result = await service.DeleteQuestionAsync(question.Id, Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task DeleteQuestionAsync_WhenTheQuizHasAttempts_FailsAsConflict()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;
        var question = (await service.AddQuestionAsync(quiz.Id, Question(), Owner, false)).Value!;
        context.QuizAttempts.Add(new QuizAttempt { QuizId = quiz.Id, StudentName = "Иван", StartedOn = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var result = await service.DeleteQuestionAsync(question.Id, Owner, false);

        Assert.Equal(ServiceError.Conflict, result.Error);
    }

    // ---------- UpdateQuestionAsync / GetQuestionAsync ----------

    [Fact]
    public async Task UpdateQuestionAsync_ReplacesTextAndOptions()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;
        var question = (await service.AddQuestionAsync(quiz.Id, Question(), Owner, false)).Value!;

        var result = await service.UpdateQuestionAsync(question.Id,
            new QuestionInput("3 + 3 = ?", new[] { "5", "6", "7" }, 1), Owner, false);

        Assert.True(result.Succeeded);
        var reloaded = (await service.GetQuestionAsync(question.Id))!;
        Assert.Equal("3 + 3 = ?", reloaded.Text);
        var options = reloaded.Options.OrderBy(o => o.Order).ToList();
        Assert.Equal(new[] { "5", "6", "7" }, options.Select(o => o.Text));
        Assert.Equal("6", options.Single(o => o.IsCorrect).Text);
    }

    [Fact]
    public async Task UpdateQuestionAsync_Missing_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        var result = await service.UpdateQuestionAsync(999, Question(), Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task UpdateQuestionAsync_ByAnotherTeacher_FailsAsForbidden()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;
        var question = (await service.AddQuestionAsync(quiz.Id, Question(), Owner, false)).Value!;

        var result = await service.UpdateQuestionAsync(question.Id, Question(text: "Друг"), Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task UpdateQuestionAsync_WithTooFewOptions_FailsAsInvalid()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;
        var question = (await service.AddQuestionAsync(quiz.Id, Question(), Owner, false)).Value!;

        var result = await service.UpdateQuestionAsync(question.Id,
            new QuestionInput("Само един", new[] { "единствен" }, 0), Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task GetQuestionAsync_ReturnsTheQuestionWithQuizAndOptions()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Practice(), Owner)).Value!;
        var question = (await service.AddQuestionAsync(quiz.Id, Question(), Owner, false)).Value!;

        var loaded = await service.GetQuestionAsync(question.Id);

        Assert.NotNull(loaded);
        Assert.Equal(quiz.Id, loaded!.Quiz.Id);
        Assert.Equal(2, loaded.Options.Count);
    }

    [Fact]
    public async Task GetQuestionAsync_Missing_ReturnsNull()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        Assert.Null(await service.GetQuestionAsync(999));
    }

    // ---------- GetByIdAsync ----------

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenMissing()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));

        Assert.Null(await service.GetByIdAsync(1));
    }

    // ---------- GetManagedAsync ----------

    [Fact]
    public async Task GetManagedAsync_WithOwner_ReturnsOnlyThatTeachersQuizzesNewestFirst()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        await service.CreateAsync(Practice("Мой 1"), Owner);
        await service.CreateAsync(Practice("Чужд"), Other);
        await service.CreateAsync(Practice("Мой 2"), Owner);

        var page = await service.GetManagedAsync(Owner, page: 1, pageSize: 10);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { "Мой 2", "Мой 1" }, page.Items.Select(i => i.Quiz.Title));
    }

    [Fact]
    public async Task GetManagedAsync_WithNullOwner_ReturnsEveryQuiz()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        await service.CreateAsync(Practice("A"), Owner);
        await service.CreateAsync(Practice("B"), Other);

        var page = await service.GetManagedAsync(null, page: 1, pageSize: 10);

        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task GetManagedAsync_CountsQuestionsAndSubmittedAttempts()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(Exam(), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id, Question(), Owner, false);
        await service.AddQuestionAsync(quiz.Id, Question("Втори"), Owner, false);
        context.QuizAttempts.Add(new QuizAttempt { QuizId = quiz.Id, StudentName = "A", StartedOn = DateTime.UtcNow, SubmittedOn = DateTime.UtcNow });
        context.QuizAttempts.Add(new QuizAttempt { QuizId = quiz.Id, StudentName = "B", StartedOn = DateTime.UtcNow }); // not submitted
        await context.SaveChangesAsync();

        var item = (await service.GetManagedAsync(Owner, 1, 10)).Items.Single();

        Assert.Equal(2, item.QuestionCount);
        Assert.Equal(1, item.AttemptCount);
    }

    [Fact]
    public async Task GetManagedAsync_Paginates()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        for (var i = 0; i < 5; i++)
        {
            await service.CreateAsync(Practice("Тест " + i), Owner);
        }

        var page = await service.GetManagedAsync(Owner, page: 2, pageSize: 2);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task GetManagedAsync_ClampsAnOutOfRangePage()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        await service.CreateAsync(Practice(), Owner);

        var page = await service.GetManagedAsync(Owner, page: 99, pageSize: 10);

        Assert.Equal(1, page.Page);
        Assert.Single(page.Items);
    }

    // ---------- QuizPermissions ----------

    [Fact]
    public void CanManage_AllowsOwnerAndAdministrator_ButNotOthers()
    {
        var quiz = new Quiz { OwnerId = Owner };

        Assert.True(QuizPermissions.CanManage(quiz, Owner, isAdministrator: false));
        Assert.True(QuizPermissions.CanManage(quiz, Other, isAdministrator: true));
        Assert.False(QuizPermissions.CanManage(quiz, Other, isAdministrator: false));
    }

    [Fact]
    public void CanManage_WithNoOwner_AllowsOnlyAdministrator()
    {
        var quiz = new Quiz { OwnerId = null };

        Assert.False(QuizPermissions.CanManage(quiz, Owner, isAdministrator: false));
        Assert.True(QuizPermissions.CanManage(quiz, Owner, isAdministrator: true));
    }

    // ---------- QuizCode ----------

    [Fact]
    public void QuizCode_Generate_UsesTheRequestedLengthAndAlphabet()
    {
        var code = QuizCode.Generate(8);

        Assert.Equal(8, code.Length);
        Assert.All(code, c => Assert.Contains(c, Alphabet));
    }

    // ---------- Question counts (own questions + bank items) ----------

    // A quiz with 1 own question, 1 pinned bank question, a draw of 2 from a section of 5 and a draw of
    // 10 from a section of 3 (capped at the 3 it holds): 1 + 1 + 2 + 3 = 7
    private static async Task<Quiz> QuizFromTheBankAsync(Data.ApplicationDbContext context, QuizService service, bool publish)
    {
        var quiz = (await service.CreateAsync(Practice("От банката"), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id, Question(), Owner, false);

        var items = new QuizItemService(context);
        var big = await SectionWithQuestionsAsync(context, "Голям", 5);
        var small = await SectionWithQuestionsAsync(context, "Малък", 3);
        await items.AddFixedQuestionAsync(quiz.Id, context.Questions.First(q => q.SectionId == big.Id).Id, Owner, false);
        await items.AddRandomSectionAsync(quiz.Id, big.Id, 2, Owner, false);
        await items.AddRandomSectionAsync(quiz.Id, small.Id, 10, Owner, false);

        if (publish)
        {
            await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        }

        return quiz;
    }

    private static async Task<Section> SectionWithQuestionsAsync(Data.ApplicationDbContext context, string name, int count)
    {
        var section = new Section { Name = name, OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();
        for (var i = 0; i < count; i++)
        {
            context.Questions.Add(new Question
            {
                SectionId = section.Id,
                Text = name + " " + i,
                Points = 1,
                Options = new List<AnswerOption> { new() { Text = "а", IsCorrect = true, Order = 1 }, new() { Text = "б", Order = 2 } }
            });
        }
        await context.SaveChangesAsync();
        return section;
    }

    [Fact]
    public async Task GetManagedAsync_CountsOwnQuestionsPinnedQuestionsAndRandomDraws()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        await QuizFromTheBankAsync(context, service, publish: false);

        var item = Assert.Single((await service.GetManagedAsync(Owner, 1)).Items);

        Assert.Equal(7, item.QuestionCount);
    }

    [Fact]
    public async Task GetPublishedPracticeAsync_CountsTheBankItemsToo()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        await QuizFromTheBankAsync(context, service, publish: true);

        var item = Assert.Single((await service.GetPublishedPracticeAsync(1)).Items);

        Assert.Equal(7, item.QuestionCount);
    }
}
