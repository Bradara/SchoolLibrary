using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Exams;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>The teacher's own points and note on the answers of a submitted class exam.</summary>
public class AttemptGradingServiceTests
{
    private const string Owner = "teacher-1";

    private sealed record Setup(ApplicationDbContext Context, QuizService Quizzes, AttemptGradingService Grading,
        QuizAttempt Attempt, Question Answered, Question Skipped);

    // Two questions of 2 points; the first is answered correctly, the second is skipped (score 2 of 4)
    private static async Task<Setup> ArrangeAsync(bool submit = true)
    {
        var context = TestDbContextFactory.Create();
        var quizzes = new QuizService(context, new QuizItemService(context));
        var quiz = (await quizzes.CreateAsync(new QuizInput("Дроби", null, QuizMode.Practice, null, null), Owner)).Value!;
        await quizzes.AddQuestionAsync(quiz.Id, new QuestionInput("1/2 + 1/2 = ?", new[] { "1", "2" }, 0, 2), Owner, false);
        await quizzes.AddQuestionAsync(quiz.Id, new QuestionInput("1/2 · 2 = ?", new[] { "1", "4" }, 0, 2), Owner, false);

        var schoolClass = (await new SchoolClassService(context).CreateAsync("5А", Owner)).Value!;
        var exam = (await new ExamAssignmentService(context).CreateAsync(
            new ExamAssignmentInput(quiz.Id, schoolClass.Id, null, null), Owner, false)).Value!;
        var attempt = (await quizzes.StartExamAsync(exam.Code, "Иван Петров", "7")).Value!;

        var questions = await context.Questions.Include(q => q.Options).OrderBy(q => q.Order).ToListAsync();
        var answered = questions[0];
        if (submit)
        {
            var right = answered.Options.Single(o => o.IsCorrect).Id;
            await quizzes.SubmitExamAsync(attempt.Token, SubmittedAnswer.FromChoices(new Dictionary<int, int?> { [answered.Id] = right }));
        }

        return new Setup(context, quizzes, new AttemptGradingService(context), attempt, answered, questions[1]);
    }

    private static async Task<QuizAttempt> ReloadAsync(Setup s)
    {
        s.Context.ChangeTracker.Clear();
        return await s.Context.QuizAttempts
            .Include(a => a.Quiz).ThenInclude(q => q.Questions).ThenInclude(q => q.Options)
            .Include(a => a.Questions).ThenInclude(aq => aq.Question).ThenInclude(q => q.Options)
            .Include(a => a.Answers).ThenInclude(a => a.Choices)
            .SingleAsync(a => a.Id == s.Attempt.Id);
    }

    [Fact]
    public async Task SetPointsAsync_ReplacesTheAutomaticPoints_AndUpdatesTheScore()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var result = await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, 1.5m, "Липсва обяснение.", Owner, false);

        Assert.True(result.Succeeded);
        var attempt = await ReloadAsync(s);
        Assert.Equal(1.5m, attempt.Score);
        Assert.Equal(4m, attempt.MaxScore);
        var answer = attempt.Answers.Single(a => a.QuestionId == s.Answered.Id);
        Assert.Equal(2m, answer.AwardedPoints);   // the automatic points are kept
        Assert.Equal(1.5m, answer.ManualPoints);
        Assert.Equal(1.5m, answer.Points);
        Assert.NotNull(answer.ManualGradedOn);

        var shown = QuizAttemptResults.Build(attempt).Questions.Single(q => q.Question.Id == s.Answered.Id);
        Assert.True(shown.IsManual);
        Assert.True(shown.IsPartial);
        Assert.Equal(1.5m, shown.AwardedPoints);
        Assert.Equal(2m, shown.AutomaticPoints);
        Assert.Equal("Липсва обяснение.", shown.ManualComment);
    }

    [Fact]
    public async Task SetPointsAsync_SkippedQuestion_GetsAnAnswerWithThePoints()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var result = await s.Grading.SetPointsAsync(s.Attempt.Id, s.Skipped.Id, 2m, null, Owner, false);

        Assert.True(result.Succeeded);
        var attempt = await ReloadAsync(s);
        Assert.Equal(4m, attempt.Score);
        var answer = attempt.Answers.Single(a => a.QuestionId == s.Skipped.Id);
        Assert.Null(answer.SelectedOptionId);
        Assert.Equal(0m, answer.AwardedPoints);
        Assert.True(QuizAttemptResults.Build(attempt).Questions.Single(q => q.Question.Id == s.Skipped.Id).IsCorrect);
    }

    [Fact]
    public async Task SetPointsAsync_NothingToSet_LeavesTheAnswerUnmarked()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var result = await s.Grading.SetPointsAsync(s.Attempt.Id, s.Skipped.Id, null, "   ", Owner, false);

        Assert.True(result.Succeeded);
        var answer = (await ReloadAsync(s)).Answers.Single(a => a.QuestionId == s.Skipped.Id);
        Assert.Null(answer.ManualPoints);
        Assert.Null(answer.ManualComment);
        Assert.Null(answer.ManualGradedOn);
    }

    [Fact]
    public async Task SetPointsAsync_OlderAttemptWithoutARowForTheQuestion_GetsOne()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        s.Context.AttemptAnswers.Remove(await s.Context.AttemptAnswers.SingleAsync(a => a.QuestionId == s.Skipped.Id));
        await s.Context.SaveChangesAsync();
        s.Context.ChangeTracker.Clear();

        var result = await s.Grading.SetPointsAsync(s.Attempt.Id, s.Skipped.Id, 1m, null, Owner, false);

        Assert.True(result.Succeeded);
        var attempt = await ReloadAsync(s);
        Assert.Equal(3m, attempt.Score);
        Assert.Equal(1m, attempt.Answers.Single(a => a.QuestionId == s.Skipped.Id).ManualPoints);
    }

    [Fact]
    public async Task SetPointsAsync_WithoutPoints_BringsBackTheAutomaticOnes_AndKeepsTheNote()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, 0m, null, Owner, false);

        await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, null, "Браво!\r\n", Owner, false);

        var attempt = await ReloadAsync(s);
        Assert.Equal(2m, attempt.Score);
        var answer = attempt.Answers.Single(a => a.QuestionId == s.Answered.Id);
        Assert.Null(answer.ManualPoints);
        Assert.Equal("Браво!", answer.ManualComment);
        var shown = QuizAttemptResults.Build(attempt).Questions.Single(q => q.Question.Id == s.Answered.Id);
        Assert.False(shown.IsManual);
        Assert.Equal("Браво!", shown.ManualComment);
    }

    [Fact]
    public async Task SetPointsAsync_EmptyNoteAndNoPoints_ClearsTheTeachersMark()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, 1m, "Бележка", Owner, false);

        await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, null, "", Owner, false);

        var answer = (await ReloadAsync(s)).Answers.Single(a => a.QuestionId == s.Answered.Id);
        Assert.Null(answer.ManualPoints);
        Assert.Null(answer.ManualComment);
        Assert.Null(answer.ManualGradedOn);
    }

    [Fact]
    public async Task SetPointsAsync_RoundsToTwoDecimals()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, 1.255m, null, Owner, false);

        Assert.Equal(1.26m, (await ReloadAsync(s)).Score);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(2.01)]
    public async Task SetPointsAsync_OutOfRange_IsInvalid(double points)
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var result = await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, (decimal)points, null, Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Точките трябва да са между 0 и 2.", result.Message);
        Assert.Equal(2m, (await ReloadAsync(s)).Score);
    }

    [Fact]
    public async Task SetPointsAsync_TooLongNote_IsInvalid()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        var result = await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, 1m,
            new string('а', AttemptAnswer.MaxCommentLength + 1), Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Null((await ReloadAsync(s)).Answers.Single(a => a.QuestionId == s.Answered.Id).ManualPoints);
    }

    [Fact]
    public async Task SetPointsAsync_AnotherTeacher_IsForbidden_ButAnAdministratorMay()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        Assert.Equal(ServiceError.Forbidden, (await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, 1m, null, "teacher-2", false)).Error);
        Assert.True((await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, 1m, null, "admin", true)).Succeeded);
    }

    [Fact]
    public async Task SetPointsAsync_AttemptNotSubmitted_IsInvalid()
    {
        var s = await ArrangeAsync(submit: false);
        using var _ = s.Context;

        var result = await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, 1m, null, Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task SetPointsAsync_UnknownAttemptOrQuestion_IsNotFound()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;

        Assert.Equal(ServiceError.NotFound, (await s.Grading.SetPointsAsync(999, s.Answered.Id, 1m, null, Owner, false)).Error);
        Assert.Equal(ServiceError.NotFound, (await s.Grading.SetPointsAsync(s.Attempt.Id, 999, 1m, null, Owner, false)).Error);
    }

    [Fact]
    public async Task SetPointsAsync_ProgramStillBeingChecked_IsInvalid()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        var answer = await s.Context.AttemptAnswers.SingleAsync(a => a.QuestionId == s.Answered.Id);
        answer.CodeCheckPending = true;
        await s.Context.SaveChangesAsync();

        var result = await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, 1m, null, Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task EditingTheQuestion_RegradesTheAutomaticPoints_ButKeepsTheTeachers()
    {
        var s = await ArrangeAsync();
        using var _ = s.Context;
        await s.Grading.SetPointsAsync(s.Attempt.Id, s.Answered.Id, 1.5m, null, Owner, false);

        // The other option becomes the right one: the student's pick is now wrong
        var ids = s.Answered.Options.OrderBy(o => o.Order).Select(o => (int?)o.Id).ToList();
        s.Context.ChangeTracker.Clear();
        var edit = await s.Quizzes.UpdateQuestionAsync(s.Answered.Id,
            new QuestionInput("1/2 + 1/2 = ?", new[] { "1", "2" }, 1, 2, OptionIds: ids), Owner, false);

        Assert.True(edit.Succeeded, edit.Message);
        var attempt = await ReloadAsync(s);
        var answer = attempt.Answers.Single(a => a.QuestionId == s.Answered.Id);
        Assert.Equal(0m, answer.AwardedPoints);
        Assert.Equal(1.5m, answer.ManualPoints);
        Assert.Equal(1.5m, attempt.Score);
    }

    [Fact]
    public void Build_TeachersPointsAboveTheQuestionsPoints_CountOnlyUpToThem()
    {
        var question = new Question { Id = 1, Points = 2, Type = QuestionType.SingleChoice, Options = new List<AnswerOption>() };
        var quiz = new Quiz { Questions = new List<Question> { question } };
        var attempt = new QuizAttempt
        {
            Quiz = quiz,
            Answers = new List<AttemptAnswer> { new() { QuestionId = 1, ManualPoints = 5m } }
        };

        var result = QuizAttemptResults.Build(attempt);

        Assert.Equal(2m, result.Score);
        Assert.True(result.Questions.Single().IsCorrect);
    }
}
