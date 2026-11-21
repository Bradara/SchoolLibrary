using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>
/// Editing a question written directly in a quiz (before the question bank) once the quiz has attempts:
/// the options are edited in place and the submitted attempts are regraded, as for bank questions.
/// Older attempts have no list of served questions and are graded against the quiz's own questions.
/// </summary>
public class DirectQuestionEditTests
{
    private const string Owner = "teacher-1";

    private static async Task<(QuizService Service, Quiz Quiz)> QuizAsync(Data.ApplicationDbContext context)
    {
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null), Owner)).Value!;
        return (service, quiz);
    }

    private static async Task<Question> AddAsync(QuizService service, Quiz quiz, QuestionInput input)
        => (await service.AddQuestionAsync(quiz.Id, input, Owner, false)).Value!;

    // An older attempt: no served-question list, only the stored answers
    private static async Task<QuizAttempt> OldAttemptAsync(Data.ApplicationDbContext context, Quiz quiz, decimal score, decimal maxScore,
        params AttemptAnswer[] answers)
    {
        var attempt = new QuizAttempt
        {
            QuizId = quiz.Id, Token = Guid.NewGuid(), StudentName = "Ученик", StartedOn = DateTime.UtcNow,
            SubmittedOn = DateTime.UtcNow, Score = score, MaxScore = maxScore
        };
        foreach (var answer in answers)
        {
            attempt.Answers.Add(answer);
        }

        context.QuizAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return attempt;
    }

    private static int[] Ids(Question question) => question.Options.OrderBy(o => o.Order).Select(o => o.Id).ToArray();

    private static async Task<QuizAttempt> ReloadAsync(Data.ApplicationDbContext context, int attemptId)
    {
        context.ChangeTracker.Clear();
        return await context.QuizAttempts.Include(a => a.Answers).SingleAsync(a => a.Id == attemptId);
    }

    [Fact]
    public async Task Edit_WhenTheQuizHasAttempts_KeepsTheIdsAndRegrades()
    {
        using var context = TestDbContextFactory.Create();
        var (service, quiz) = await QuizAsync(context);
        var question = await AddAsync(service, quiz, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0));
        var ids = Ids(question);
        var attempt = await OldAttemptAsync(context, quiz, 0, 1,
            new AttemptAnswer { QuestionId = question.Id, SelectedOptionId = ids[1], IsCorrect = false, AwardedPoints = 0 });

        // The key was wrong: "5" is correct, and the question is worth 2 points
        var edit = new QuestionInput("2 + 3 = ?", new[] { "4", "5" }, 1, 2, OptionIds: ids.Select(i => (int?)i).ToList());
        var result = await service.UpdateQuestionAsync(question.Id, edit, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(ids, Ids((await service.GetQuestionAsync(question.Id))!));
        var regraded = await ReloadAsync(context, attempt.Id);
        Assert.Equal(2m, regraded.Score);
        Assert.Equal(2m, regraded.MaxScore);
        var answer = regraded.Answers.Single();
        Assert.True(answer.IsCorrect);
        Assert.Equal(2m, answer.AwardedPoints);
    }

    [Fact]
    public async Task Edit_ASkippedQuestion_StillChangesTheMaximum()
    {
        using var context = TestDbContextFactory.Create();
        var (service, quiz) = await QuizAsync(context);
        var answered = await AddAsync(service, quiz, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0));
        var skipped = await AddAsync(service, quiz, new QuestionInput("3 + 3 = ?", new[] { "6", "7" }, 0));
        var attempt = await OldAttemptAsync(context, quiz, 1, 2,
            new AttemptAnswer { QuestionId = answered.Id, SelectedOptionId = Ids(answered)[0], IsCorrect = true, AwardedPoints = 1 });

        var edit = new QuestionInput("3 + 3 = ?", new[] { "6", "7" }, 0, 4, OptionIds: Ids(skipped).Select(i => (int?)i).ToList());
        var result = await service.UpdateQuestionAsync(skipped.Id, edit, Owner, false);

        Assert.True(result.Succeeded);
        var regraded = await ReloadAsync(context, attempt.Id);
        Assert.Equal(1m, regraded.Score);
        Assert.Equal(5m, regraded.MaxScore);
    }

    [Fact]
    public async Task Edit_RemovingAChosenOption_FailsAsConflict()
    {
        using var context = TestDbContextFactory.Create();
        var (service, quiz) = await QuizAsync(context);
        var question = await AddAsync(service, quiz, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0));
        var ids = Ids(question);
        await OldAttemptAsync(context, quiz, 0, 1, new AttemptAnswer { QuestionId = question.Id, SelectedOptionId = ids[1] });

        var edit = new QuestionInput("2 + 2 = ?", new[] { "4", "6" }, 0, OptionIds: new int?[] { ids[0], null });
        var result = await service.UpdateQuestionAsync(question.Id, edit, Owner, false);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Contains("„5“", result.Message);
    }

    [Fact]
    public async Task Edit_ChangingTheType_FailsAsConflict()
    {
        using var context = TestDbContextFactory.Create();
        var (service, quiz) = await QuizAsync(context);
        var question = await AddAsync(service, quiz, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0));
        await OldAttemptAsync(context, quiz, 0, 1);

        var result = await service.UpdateQuestionAsync(question.Id, QuestionInput.ShortAnswer("2 + 2 = ?", new[] { "4" }), Owner, false);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal(QuestionType.SingleChoice, (await service.GetQuestionAsync(question.Id))!.Type);
    }

    [Fact]
    public async Task Edit_LeavesAttemptsOfOtherQuizzesAlone()
    {
        using var context = TestDbContextFactory.Create();
        var (service, quiz) = await QuizAsync(context);
        var question = await AddAsync(service, quiz, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0));
        await OldAttemptAsync(context, quiz, 1, 1,
            new AttemptAnswer { QuestionId = question.Id, SelectedOptionId = Ids(question)[0], IsCorrect = true, AwardedPoints = 1 });
        var (_, other) = await QuizAsync(context);
        var untouched = await OldAttemptAsync(context, other, 7, 9);

        var edit = new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0, 3, OptionIds: Ids(question).Select(i => (int?)i).ToList());
        await service.UpdateQuestionAsync(question.Id, edit, Owner, false);

        var reloaded = await ReloadAsync(context, untouched.Id);
        Assert.Equal(7m, reloaded.Score);
        Assert.Equal(9m, reloaded.MaxScore);
    }

    [Fact]
    public async Task CountAnsweredAttempts_CountsOlderAttemptsOfTheQuiz()
    {
        using var context = TestDbContextFactory.Create();
        var (service, quiz) = await QuizAsync(context);
        var question = await AddAsync(service, quiz, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0));
        await OldAttemptAsync(context, quiz, 0, 1);
        await OldAttemptAsync(context, quiz, 1, 1,
            new AttemptAnswer { QuestionId = question.Id, SelectedOptionId = Ids(question)[0], IsCorrect = true, AwardedPoints = 1 });

        var count = await new QuestionBankService(context).CountAnsweredAttemptsAsync(question.Id);

        Assert.Equal(2, count);
    }
}
