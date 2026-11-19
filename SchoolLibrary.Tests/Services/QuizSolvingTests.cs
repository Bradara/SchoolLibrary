using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class QuizSolvingTests
{
    private const string Owner = "teacher-1";

    // A quiz built by hand (no database), for the pure grading logic.
    private static Quiz TwoQuestionQuiz()
        => new()
        {
            Questions = new List<Question>
            {
                new()
                {
                    Id = 1, Order = 1,
                    Options = new List<AnswerOption> { new() { Id = 10, IsCorrect = true }, new() { Id = 11 } }
                },
                new()
                {
                    Id = 2, Order = 2,
                    Options = new List<AnswerOption> { new() { Id = 20 }, new() { Id = 21, IsCorrect = true } }
                }
            }
        };

    // ---------- QuizGrading (pure) ----------

    [Fact]
    public void Grade_AllCorrect_GivesTheFullScore()
    {
        var answers = new Dictionary<int, int?> { [1] = 10, [2] = 21 };

        var result = QuizGrading.Grade(TwoQuestionQuiz(), answers);

        Assert.Equal(2, result.Score);
        Assert.Equal(2, result.MaxScore);
        Assert.All(result.Questions, q => Assert.True(q.IsCorrect));
    }

    [Fact]
    public void Grade_ReportsWrongAndUnansweredPerQuestion()
    {
        // Q1 wrong (picked 11, correct 10); Q2 left unanswered
        var answers = new Dictionary<int, int?> { [1] = 11 };

        var result = QuizGrading.Grade(TwoQuestionQuiz(), answers);

        Assert.Equal(0, result.Score);

        var q1 = result.Questions[0];
        Assert.False(q1.IsCorrect);
        Assert.Equal(11, q1.SelectedOptionId);
        Assert.Equal(10, q1.CorrectOptionId);

        var q2 = result.Questions[1];
        Assert.False(q2.IsCorrect);
        Assert.Null(q2.SelectedOptionId);
        Assert.Equal(21, q2.CorrectOptionId);
    }

    [Fact]
    public void Grade_KeepsTheQuestionOrder()
    {
        var result = QuizGrading.Grade(TwoQuestionQuiz(), new Dictionary<int, int?>());

        Assert.Equal(new[] { 1, 2 }, result.Questions.Select(q => q.Question.Id));
    }

    // ---------- Service: solving a practice quiz ----------

    private static async Task<Quiz> PublishedPracticeAsync(QuizService service, int questions = 2)
    {
        var quiz = (await service.CreateAsync(new QuizInput("Упражнение", null, QuizMode.Practice, null, null), Owner)).Value!;
        for (var i = 0; i < questions; i++)
        {
            await service.AddQuestionAsync(quiz.Id, new QuestionInput("Въпрос " + i, new[] { "верен", "грешен" }, 0), Owner, false);
        }

        await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        return quiz;
    }

    [Fact]
    public async Task GetPracticeForSolvingAsync_ReturnsPublishedPracticeWithQuestionsAndOptions()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var quiz = await PublishedPracticeAsync(service);

        var loaded = await service.GetPracticeForSolvingAsync(quiz.Id);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Questions.Count);
        Assert.All(loaded.Questions, q => Assert.Equal(2, q.Options.Count));
    }

    [Fact]
    public async Task GetPracticeForSolvingAsync_ReturnsNull_ForUnpublishedQuiz()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var quiz = (await service.CreateAsync(new QuizInput("Чернова", null, QuizMode.Practice, null, null), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id, new QuestionInput("В", new[] { "а", "б" }, 0), Owner, false);
        // not published

        Assert.Null(await service.GetPracticeForSolvingAsync(quiz.Id));
    }

    [Fact]
    public async Task GetPracticeForSolvingAsync_ReturnsNull_ForAnExam()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var quiz = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id, new QuestionInput("В", new[] { "а", "б" }, 0), Owner, false);
        await service.SetPublishedAsync(quiz.Id, true, Owner, false);

        Assert.Null(await service.GetPracticeForSolvingAsync(quiz.Id));
    }

    [Fact]
    public async Task GetPracticeForSolvingAsync_ReturnsNull_WhenMissing()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);

        Assert.Null(await service.GetPracticeForSolvingAsync(999));
    }

    [Fact]
    public async Task GradePracticeAsync_GradesThePickedAnswers()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var quiz = await PublishedPracticeAsync(service);
        var loaded = (await service.GetPracticeForSolvingAsync(quiz.Id))!;

        // Answer the first question correctly (the option marked correct), skip the second
        var first = loaded.Questions.First();
        var correctId = first.Options.Single(o => o.IsCorrect).Id;
        var answers = new Dictionary<int, int?> { [first.Id] = correctId };

        var result = await service.GradePracticeAsync(quiz.Id, answers);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.Score);
        Assert.Equal(2, result.Value!.MaxScore);
    }

    [Fact]
    public async Task GradePracticeAsync_Unpublished_FailsAsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        var quiz = (await service.CreateAsync(new QuizInput("Чернова", null, QuizMode.Practice, null, null), Owner)).Value!;

        var result = await service.GradePracticeAsync(quiz.Id, new Dictionary<int, int?>());

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    // ---------- Service: browsing practice quizzes ----------

    [Fact]
    public async Task GetPublishedPracticeAsync_ReturnsOnlyPublishedPractice_WithQuestionCounts()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);

        var published = await PublishedPracticeAsync(service, questions: 3);

        // an unpublished practice quiz and a published exam must not appear
        await service.CreateAsync(new QuizInput("Чернова", null, QuizMode.Practice, null, null), Owner);
        var exam = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null), Owner)).Value!;
        await service.AddQuestionAsync(exam.Id, new QuestionInput("В", new[] { "а", "б" }, 0), Owner, false);
        await service.SetPublishedAsync(exam.Id, true, Owner, false);

        var page = await service.GetPublishedPracticeAsync(1, 10);

        var item = Assert.Single(page.Items);
        Assert.Equal(published.Id, item.Quiz.Id);
        Assert.Equal(3, item.QuestionCount);
    }

    [Fact]
    public async Task GetPublishedPracticeAsync_OrdersByTitleAndPaginates()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context);
        foreach (var title in new[] { "Веднъж", "Алфа", "Бета" })
        {
            var quiz = (await service.CreateAsync(new QuizInput(title, null, QuizMode.Practice, null, null), Owner)).Value!;
            await service.AddQuestionAsync(quiz.Id, new QuestionInput("В", new[] { "а", "б" }, 0), Owner, false);
            await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        }

        var firstPage = await service.GetPublishedPracticeAsync(page: 1, pageSize: 2);

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(new[] { "Алфа", "Бета" }, firstPage.Items.Select(i => i.Quiz.Title));
    }
}
