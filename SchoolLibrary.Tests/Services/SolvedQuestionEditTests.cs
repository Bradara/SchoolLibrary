using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Services;

/// <summary>
/// Editing a bank question that students have answered: the options are edited in place (so the stored answers
/// keep pointing at them) and the submitted attempts are regraded.
/// </summary>
public class SolvedQuestionEditTests
{
    private const string Owner = "teacher-1";

    private static async Task<(QuestionBankService Service, Question Question)> QuestionAsync(Data.ApplicationDbContext context, QuestionInput input)
    {
        var section = new Section { Name = "Дроби", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();
        var service = new QuestionBankService(context);
        var question = (await service.CreateAsync(section.Id, input, Owner, false)).Value!;
        return (service, question);
    }

    // A submitted attempt that was served the question and answered it as given
    private static async Task<QuizAttempt> AnsweredAsync(Data.ApplicationDbContext context, Question question, AttemptAnswer answer, bool submitted = true)
    {
        var quiz = new Quiz { Title = "Изпит", OwnerId = Owner };
        context.Quizzes.Add(quiz);
        await context.SaveChangesAsync();
        answer.QuestionId = question.Id;
        var attempt = new QuizAttempt
        {
            QuizId = quiz.Id, Token = Guid.NewGuid(), StudentName = "Ученик", StartedOn = DateTime.UtcNow,
            SubmittedOn = submitted ? DateTime.UtcNow : null, Score = answer.AwardedPoints, MaxScore = question.Points
        };
        attempt.Questions.Add(new AttemptQuestion { QuestionId = question.Id, Order = 1 });
        attempt.Answers.Add(answer);
        context.QuizAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return attempt;
    }

    private static int[] Ids(Question question) => question.Options.OrderBy(o => o.Order).Select(o => o.Id).ToArray();

    private static async Task<QuizAttempt> ReloadAsync(Data.ApplicationDbContext context, int attemptId)
    {
        context.ChangeTracker.Clear();
        return await context.QuizAttempts.Include(a => a.Answers).ThenInclude(a => a.Choices).SingleAsync(a => a.Id == attemptId);
    }

    // ---------- Single choice ----------

    [Fact]
    public async Task Edit_ChangingTheCorrectOption_KeepsTheIdsAndRegrades()
    {
        using var context = TestDbContextFactory.Create();
        var (service, question) = await QuestionAsync(context, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0, 2));
        var ids = Ids(question);
        var attempt = await AnsweredAsync(context, question,
            new AttemptAnswer { SelectedOptionId = ids[0], IsCorrect = true, AwardedPoints = 2 });

        // The teacher fixes the key: "5" is correct now, and the question is worth 3 points
        var edit = new QuestionInput("2 + 3 = ?", new[] { "4", "5" }, 1, 3, OptionIds: ids.Select(i => (int?)i).ToList());
        var result = await service.UpdateAsync(question.Id, edit, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(ids, Ids((await service.GetQuestionAsync(question.Id))!));
        var regraded = await ReloadAsync(context, attempt.Id);
        Assert.Equal(0m, regraded.Score);
        Assert.Equal(3m, regraded.MaxScore);
        var answer = regraded.Answers.Single();
        Assert.Equal(ids[0], answer.SelectedOptionId);
        Assert.False(answer.IsCorrect);
        Assert.Equal(0m, answer.AwardedPoints);
    }

    [Fact]
    public async Task Edit_AddingAnOption_AndRemovingAnUnchosenOne_IsAllowed()
    {
        using var context = TestDbContextFactory.Create();
        var (service, question) = await QuestionAsync(context, new QuestionInput("Столица?", new[] { "София", "Варна", "Русе" }, 0));
        var ids = Ids(question);
        var attempt = await AnsweredAsync(context, question,
            new AttemptAnswer { SelectedOptionId = ids[0], IsCorrect = true, AwardedPoints = 1 });

        var edit = new QuestionInput("Столица?", new[] { "София", "Бургас", "Пловдив" }, 0,
            OptionIds: new int?[] { ids[0], ids[1], null });
        var result = await service.UpdateAsync(question.Id, edit, Owner, false);

        Assert.True(result.Succeeded);
        var saved = (await service.GetQuestionAsync(question.Id))!.Options.OrderBy(o => o.Order).ToList();
        Assert.Equal(new[] { "София", "Бургас", "Пловдив" }, saved.Select(o => o.Text));
        Assert.Equal(ids[0], saved[0].Id);
        Assert.Equal(ids[1], saved[1].Id);
        Assert.DoesNotContain(ids[2], saved.Select(o => o.Id));
        Assert.Equal(1m, (await ReloadAsync(context, attempt.Id)).Score);
    }

    [Fact]
    public async Task Edit_RemovingAChosenOption_FailsAsConflict()
    {
        using var context = TestDbContextFactory.Create();
        var (service, question) = await QuestionAsync(context, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0));
        var ids = Ids(question);
        await AnsweredAsync(context, question, new AttemptAnswer { SelectedOptionId = ids[1] });

        var edit = new QuestionInput("2 + 2 = ?", new[] { "4", "6" }, 0, OptionIds: new int?[] { ids[0], null });
        var result = await service.UpdateAsync(question.Id, edit, Owner, false);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Contains("„5“", result.Message);
        Assert.Equal(new[] { "4", "5" }, (await service.GetQuestionAsync(question.Id))!.Options.OrderBy(o => o.Order).Select(o => o.Text));
    }

    [Fact]
    public async Task Edit_ChangingTheType_FailsAsConflict()
    {
        using var context = TestDbContextFactory.Create();
        var (service, question) = await QuestionAsync(context, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0));
        await AnsweredAsync(context, question, new AttemptAnswer { SelectedOptionId = Ids(question)[0] });

        var result = await service.UpdateAsync(question.Id, QuestionInput.ShortAnswer("2 + 2 = ?", new[] { "4" }), Owner, false);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal(QuestionType.SingleChoice, (await service.GetQuestionAsync(question.Id))!.Type);
    }

    [Fact]
    public async Task Edit_WhenOnlyServed_KeepsIds_AndLeavesTheOpenAttemptAlone()
    {
        using var context = TestDbContextFactory.Create();
        var (service, question) = await QuestionAsync(context, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0));
        var ids = Ids(question);
        var attempt = await AnsweredAsync(context, question, new AttemptAnswer { SelectedOptionId = ids[0], AwardedPoints = 1, IsCorrect = true }, submitted: false);

        var edit = new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 1, OptionIds: ids.Select(i => (int?)i).ToList());
        Assert.True((await service.UpdateAsync(question.Id, edit, Owner, false)).Succeeded);

        Assert.Equal(ids, Ids((await service.GetQuestionAsync(question.Id))!));
        Assert.Equal(1m, (await ReloadAsync(context, attempt.Id)).Score);
    }

    // ---------- Other types ----------

    [Fact]
    public async Task Edit_MultipleChoice_KeepsTheTicksAndRegrades()
    {
        using var context = TestDbContextFactory.Create();
        var (service, question) = await QuestionAsync(context,
            QuestionInput.MultipleChoice("Прости числа?", new[] { "2", "3", "4" }, new[] { 0, 1 }, 2));
        var ids = Ids(question);
        var answer = new AttemptAnswer { IsCorrect = false, AwardedPoints = 1 };
        answer.Choices.Add(new AttemptAnswerChoice { OptionId = ids[0] });
        var attempt = await AnsweredAsync(context, question, answer);

        // Only "2" is correct now, so ticking just "2" earns everything
        var edit = QuestionInput.MultipleChoice("Четни прости числа?", new[] { "2", "3", "4" }, new[] { 0 }, 2)
            with { OptionIds = ids.Select(i => (int?)i).ToList() };
        Assert.True((await service.UpdateAsync(question.Id, edit, Owner, false)).Succeeded);

        var regraded = await ReloadAsync(context, attempt.Id);
        Assert.Equal(2m, regraded.Score);
        Assert.True(regraded.Answers.Single().IsCorrect);
        Assert.Equal(ids[0], regraded.Answers.Single().Choices.Single().OptionId);
    }

    [Fact]
    public async Task Edit_MultipleChoice_RemovingATickedOption_FailsAsConflict()
    {
        using var context = TestDbContextFactory.Create();
        var (service, question) = await QuestionAsync(context,
            QuestionInput.MultipleChoice("Прости числа?", new[] { "2", "3", "4" }, new[] { 0, 1 }));
        var ids = Ids(question);
        var answer = new AttemptAnswer();
        answer.Choices.Add(new AttemptAnswerChoice { OptionId = ids[2] });
        await AnsweredAsync(context, question, answer);

        var edit = QuestionInput.MultipleChoice("Прости числа?", new[] { "2", "3" }, new[] { 0, 1 })
            with { OptionIds = new int?[] { ids[0], ids[1] } };

        Assert.Equal(ServiceError.Conflict, (await service.UpdateAsync(question.Id, edit, Owner, false)).Error);
    }

    [Fact]
    public async Task Edit_ShortAnswer_RegradesTheTypedText()
    {
        using var context = TestDbContextFactory.Create();
        var (service, question) = await QuestionAsync(context, QuestionInput.ShortAnswer("Столица?", new[] { "София" }));
        var attempt = await AnsweredAsync(context, question, new AttemptAnswer { ResponseText = "Sofia" });

        var edit = QuestionInput.ShortAnswer("Столица?", new[] { "София", "Sofia" }) with { OptionIds = new int?[] { Ids(question)[0], null } };
        Assert.True((await service.UpdateAsync(question.Id, edit, Owner, false)).Succeeded);

        Assert.Equal(1m, (await ReloadAsync(context, attempt.Id)).Score);
    }

    [Fact]
    public async Task Edit_TrueFalse_FlipsTheAnswer_AndRegrades()
    {
        using var context = TestDbContextFactory.Create();
        var (service, question) = await QuestionAsync(context, QuestionInput.TrueFalse("Земята е плоска.", true));
        var ids = Ids(question);
        var attempt = await AnsweredAsync(context, question, new AttemptAnswer { SelectedOptionId = ids[1] });

        // The form sends no option ids for true/false: the two options are kept in their order
        Assert.True((await service.UpdateAsync(question.Id, QuestionInput.TrueFalse("Земята е плоска.", false), Owner, false)).Succeeded);

        Assert.Equal(ids, Ids((await service.GetQuestionAsync(question.Id))!));
        Assert.Equal(1m, (await ReloadAsync(context, attempt.Id)).Score);
    }

    [Fact]
    public async Task Edit_Matching_KeepsPicksByText_AndDropsPicksNoLongerOffered()
    {
        using var context = TestDbContextFactory.Create();
        var (service, question) = await QuestionAsync(context,
            QuestionInput.Matching("Столици", new[] { ("България", "София"), ("Франция", "Париж"), ("", "Берлин") }));
        var ids = Ids(question);
        var answer = new AttemptAnswer();
        answer.Choices.Add(new AttemptAnswerChoice { OptionId = ids[0], MatchedOptionId = ids[0] });   // България → София
        answer.Choices.Add(new AttemptAnswerChoice { OptionId = ids[1], MatchedOptionId = ids[2] });   // Франция → Берлин
        var attempt = await AnsweredAsync(context, question, answer);

        // "София" moves to a new row's answer and "Берлин" is no longer offered
        var edit = QuestionInput.Matching("Столици", new[] { ("България", "Sofia"), ("Франция", "Париж"), ("", "София") })
            with { OptionIds = new int?[] { ids[0], ids[1], null } };
        Assert.True((await service.UpdateAsync(question.Id, edit, Owner, false)).Succeeded);

        var regraded = await ReloadAsync(context, attempt.Id);
        var saved = (await service.GetQuestionAsync(question.Id))!.Options.OrderBy(o => o.Order).ToList();
        var pick = Assert.Single(regraded.Answers.Single().Choices);
        Assert.Equal(ids[0], pick.OptionId);
        Assert.Equal(saved[2].Id, pick.MatchedOptionId);
        Assert.Equal(0m, regraded.Score);
    }

    // ---------- Count and helpers ----------

    [Fact]
    public async Task CountAnsweredAttempts_CountsSubmittedAttemptsOnly()
    {
        using var context = TestDbContextFactory.Create();
        var (service, question) = await QuestionAsync(context, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0));
        await AnsweredAsync(context, question, new AttemptAnswer());
        await AnsweredAsync(context, question, new AttemptAnswer());
        await AnsweredAsync(context, question, new AttemptAnswer(), submitted: false);

        Assert.Equal(2, await service.CountAnsweredAttemptsAsync(question.Id));
    }

    [Fact]
    public void KeptOptionIds_IgnoresForeignAndRepeatedIds()
    {
        var question = new Question { Options = new List<AnswerOption> { new() { Id = 1, Order = 1 }, new() { Id = 2, Order = 2 } } };
        var input = new QuestionInput("?", new[] { "а", "б", "в", "г" }, 0, OptionIds: new int?[] { 2, 99, 2, null });

        Assert.Equal(new int?[] { 2, null, null, null }, QuestionContent.KeptOptionIds(question, input));
    }

    [Fact]
    public void Form_CarriesOptionIdsAlongTheKeptRows()
    {
        var form = QuestionFormViewModel.FromQuestion(new Question
        {
            Type = QuestionType.SingleChoice, Text = "?",
            Options = new List<AnswerOption> { new() { Id = 7, Text = "а", Order = 1, IsCorrect = true }, new() { Id = 8, Text = "б", Order = 2 } }
        });
        Assert.Equal(new int?[] { 7, 8 }, form.OptionIds);

        // The first row is cleared and a new row added: the ids follow the rows that stay
        form.Options = new List<string> { "", "б", "в" };
        form.OptionIds = new List<int?> { 7, 8, null };
        form.CorrectIndex = 1;

        var input = form.ToInput();
        Assert.Equal(new[] { "б", "в" }, input.Options);
        Assert.Equal(new int?[] { 8, null }, input.OptionIds);
    }

    [Fact]
    public void Form_Matching_CarriesOptionIds()
    {
        var form = new QuestionFormViewModel
        {
            Type = QuestionType.Matching, Text = "?",
            Options = new List<string> { "1", "", "2" },
            Matches = new List<string> { "едно", "", "две" },
            OptionIds = new List<int?> { 5, 6, null }
        };

        Assert.Equal(new int?[] { 5, null }, form.ToInput().OptionIds);
    }
}
