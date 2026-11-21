using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Services;

/// <summary>Multiple-choice questions end to end: shares, validation, grading, storage, results and the form.</summary>
public class MultipleChoiceQuestionTests
{
    private const string Owner = "teacher-1";

    // ---------- QuestionInput.MultipleChoice (equal shares) ----------

    [Fact]
    public void Input_TwoCorrectOfFour_SplitsThePointsAndPenalisesTheWrongOnes()
    {
        var input = QuestionInput.MultipleChoice("Кои са прости?", new[] { "2", "3", "4", "6" }, new[] { 0, 1 }, 2);

        Assert.Equal(QuestionType.MultipleChoice, input.Type);
        Assert.Equal(new[] { 50m, 50m, -50m, -50m }, input.Fractions);
        Assert.Equal(-1, input.CorrectIndex);
        Assert.Equal(2, input.Points);
        Assert.Null(QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Input_ThreeCorrect_RoundsTheShares()
    {
        var input = QuestionInput.MultipleChoice("Кои?", new[] { "а", "б", "в", "г" }, new[] { 0, 1, 3 });

        Assert.Equal(new[] { 33.33m, 33.33m, -33.33m, 33.33m }, input.Fractions);
    }

    [Fact]
    public void Input_IgnoresIndexesOutsideTheOptions()
    {
        var input = QuestionInput.MultipleChoice("Кои?", new[] { "а", "б" }, new[] { 1, 5, -1 });

        Assert.Equal(new[] { -100m, 100m }, input.Fractions);
    }

    // ---------- Validation ----------

    [Fact]
    public void Validate_NoCorrectOption_IsRejected()
    {
        var input = QuestionInput.MultipleChoice("Кои?", new[] { "а", "б" }, Array.Empty<int>());

        Assert.Equal("Отбележете поне един верен отговор.", QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_ShareOutOfRange_IsRejected()
    {
        var input = new QuestionInput("Кои?", new[] { "а", "б" }, -1, 1, QuestionType.MultipleChoice, new[] { 150m, 0m });

        Assert.Equal("Делът на отговор трябва да е между -100% и 100%.", QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_MissingShares_AreRejected()
    {
        var withoutShares = new QuestionInput("Кои?", new[] { "а", "б" }, 0, 1, QuestionType.MultipleChoice);
        var tooFew = new QuestionInput("Кои?", new[] { "а", "б", "в" }, -1, 1, QuestionType.MultipleChoice, new[] { 100m, 0m });

        Assert.Equal("Посочете дела на всеки отговор.", QuizService.ValidateQuestion(withoutShares));
        Assert.Equal("Посочете дела на всеки отговор.", QuizService.ValidateQuestion(tooFew));
    }

    [Fact]
    public void Validate_SharesOnASingleChoiceQuestion_AreRejected()
    {
        var input = new QuestionInput("2 + 2?", new[] { "4", "5" }, 0, 1, QuestionType.SingleChoice, new[] { 100m, 0m });

        Assert.Equal("Неподдържан вид въпрос.", QuizService.ValidateQuestion(input));
    }

    // ---------- MultipleChoiceGrader ----------

    // Options 1 and 2 are correct (50% each); 3 and 4 are wrong (-50% each); the question is worth 4 points
    private static Question Question(params decimal[] fractions) => new()
    {
        Id = 7,
        Type = QuestionType.MultipleChoice,
        Points = 4,
        Options = (fractions.Length > 0 ? fractions : new[] { 50m, 50m, -50m, -50m })
            .Select((f, i) => new AnswerOption { Id = i + 1, Text = "о" + (i + 1), Fraction = f, Order = i + 1 })
            .ToList()
    };

    private static QuestionGrade Grade(Question question, params int[] ticked)
        => new MultipleChoiceGrader().Grade(question, SubmittedAnswer.Choices(ticked));

    [Fact]
    public void Grader_IsRegistered()
    {
        Assert.IsType<MultipleChoiceGrader>(QuestionGraders.For(QuestionType.MultipleChoice));
    }

    [Fact]
    public void Grader_AllCorrectTicked_EarnsAllPoints()
    {
        var grade = Grade(Question(), 1, 2);

        Assert.Equal(4m, grade.AwardedPoints);
        Assert.True(grade.IsCorrect);
        Assert.Equal(new[] { 1, 2 }, grade.Answer.TickedOptionIds);
    }

    [Fact]
    public void Grader_SomeCorrectTicked_EarnsTheirShare()
    {
        var grade = Grade(Question(), 2);

        Assert.Equal(2m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
    }

    [Fact]
    public void Grader_AWrongTick_TakesAwayItsShare()
    {
        // 50 + 50 - 50 = 50% of 4 points
        var grade = Grade(Question(), 1, 2, 3);

        Assert.Equal(2m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
    }

    [Fact]
    public void Grader_TickingEverything_EarnsNothing()
    {
        var grade = Grade(Question(), 1, 2, 3, 4);

        Assert.Equal(0m, grade.AwardedPoints);
    }

    [Fact]
    public void Grader_NeverGoesBelowZero()
    {
        var grade = Grade(Question(), 3, 4);

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
    }

    [Fact]
    public void Grader_RoundedShares_StillEarnFullCredit()
    {
        // 3 × 33.33 = 99.99, compared with the same total, is a full answer
        var grade = Grade(Question(33.33m, 33.33m, 33.33m, -100m), 1, 2, 3);

        Assert.Equal(4m, grade.AwardedPoints);
        Assert.True(grade.IsCorrect);
    }

    [Fact]
    public void Grader_SharesNotAddingUpTo100_AreScaled()
    {
        // Two correct options at 40% each: both ticked is still a full answer
        var grade = Grade(Question(40m, 40m, 0m), 1);

        Assert.Equal(2m, grade.AwardedPoints);
        Assert.Equal(4m, Grade(Question(40m, 40m, 0m), 1, 2).AwardedPoints);
    }

    [Fact]
    public void Grader_IgnoresForeignAndRepeatedOptions_AndKeepsTheQuestionOrder()
    {
        var grade = new MultipleChoiceGrader().Grade(Question(), new SubmittedAnswer(OptionIds: new[] { 2, 99, 1, 2 }));

        Assert.Equal(4m, grade.AwardedPoints);
        Assert.Equal(new[] { 1, 2 }, grade.Answer.TickedOptionIds);
    }

    [Fact]
    public void Grader_NothingTicked_IsUnanswered()
    {
        var grade = new MultipleChoiceGrader().Grade(Question(), SubmittedAnswer.Empty);

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.Same(SubmittedAnswer.Empty, grade.Answer);
    }

    [Fact]
    public void Grader_OnlyForeignOptions_IsUnanswered()
    {
        Assert.Same(SubmittedAnswer.Empty, Grade(Question(), 50, 51).Answer);
    }

    // ---------- SubmittedAnswer ----------

    [Fact]
    public void Choices_RemovesDuplicates_AndIsEmptyWithoutTicks()
    {
        Assert.Equal(new[] { 3, 1 }, SubmittedAnswer.Choices(new[] { 3, 1, 3 }).TickedOptionIds);
        Assert.Same(SubmittedAnswer.Empty, SubmittedAnswer.Choices(Array.Empty<int>()));
        Assert.Same(SubmittedAnswer.Empty, SubmittedAnswer.Choices(null));
        Assert.Empty(SubmittedAnswer.Empty.TickedOptionIds);
    }

    [Fact]
    public void FromForm_CombinesPicksAndTicks()
    {
        var answers = SubmittedAnswer.FromForm(
            new Dictionary<int, int?> { [1] = 10, [2] = 20 },
            new Dictionary<int, List<int>> { [2] = new() { 21, 22 }, [3] = new() { 31 } });

        Assert.Equal(10, answers[1].OptionId);
        Assert.Equal(new[] { 21, 22 }, answers[2].TickedOptionIds);   // ticks win
        Assert.Null(answers[2].OptionId);
        Assert.Equal(new[] { 31 }, answers[3].TickedOptionIds);
        Assert.Empty(SubmittedAnswer.FromForm(null, null));
    }

    // ---------- Storage and results ----------

    private static async Task<(QuizService Service, Quiz Quiz)> ExamWithMultipleChoiceAsync(Data.ApplicationDbContext context)
    {
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id,
            QuestionInput.MultipleChoice("Кои са прости?", new[] { "2", "3", "4", "6" }, new[] { 0, 1 }, 4), Owner, false);
        await service.AddQuestionAsync(quiz.Id, new QuestionInput("2 + 2?", new[] { "4", "5" }, 0), Owner, false);
        await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        return (service, (await service.GetByIdAsync(quiz.Id))!);
    }

    [Fact]
    public async Task AddQuestionAsync_StoresTheShares()
    {
        using var context = TestDbContextFactory.Create();
        var (_, quiz) = await ExamWithMultipleChoiceAsync(context);

        var saved = quiz.Questions.Single(q => q.Type == QuestionType.MultipleChoice);
        Assert.Equal(new[] { 50m, 50m, -50m, -50m }, saved.Options.OrderBy(o => o.Order).Select(o => o.Fraction));
        Assert.Equal(4, saved.Points);
    }

    [Fact]
    public async Task SubmitExamAsync_StoresTheTicksAndRebuildsTheSameResult()
    {
        using var context = TestDbContextFactory.Create();
        var (service, exam) = await ExamWithMultipleChoiceAsync(context);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var loaded = (await service.GetExamAttemptAsync(attempt.Token))!;
        var multi = loaded.Questions.Select(aq => aq.Question).Single(q => q.Type == QuestionType.MultipleChoice);
        var single = loaded.Questions.Select(aq => aq.Question).Single(q => q.Type == QuestionType.SingleChoice);
        var byText = multi.Options.ToDictionary(o => o.Text, o => o.Id);

        // One correct and one wrong tick: 50 - 50 = 0 of 4; the single-choice question is right: 1 of 1
        var answers = new Dictionary<int, SubmittedAnswer>
        {
            [multi.Id] = SubmittedAnswer.Choices(new[] { byText["2"], byText["4"] }),
            [single.Id] = SubmittedAnswer.Choice(single.Options.Single(o => o.IsCorrect).Id)
        };
        var submitted = await service.SubmitExamAsync(attempt.Token, answers);

        Assert.Equal(1m, submitted.Value!.Score);
        Assert.Equal(5m, submitted.Value.MaxScore);

        var stored = await context.AttemptAnswers.Include(a => a.Choices)
            .SingleAsync(a => a.AttemptId == attempt.Id && a.QuestionId == multi.Id);
        Assert.Null(stored.SelectedOptionId);
        Assert.Equal(new[] { byText["2"], byText["4"] }.Order(), stored.Choices.Select(c => c.OptionId).Order());

        // The result page rebuilds the attempt from what was stored
        var rebuilt = (await service.GetExamResultAsync(attempt.Token)).Value!;
        Assert.Equal(1m, rebuilt.Score);
        var multiResult = rebuilt.Questions.Single(r => r.Question.Id == multi.Id);
        Assert.True(multiResult.IsChosen(byText["2"]));
        Assert.True(multiResult.IsChosen(byText["4"]));
        Assert.False(multiResult.IsChosen(byText["3"]));
        Assert.Equal(0m, multiResult.AwardedPoints);
    }

    [Fact]
    public async Task SubmitExamAsync_PartialAnswer_IsMarkedPartial()
    {
        using var context = TestDbContextFactory.Create();
        var (service, exam) = await ExamWithMultipleChoiceAsync(context);
        var attempt = (await service.StartExamAsync(exam.Code!, "Мария", "7")).Value!;
        var multi = (await service.GetExamAttemptAsync(attempt.Token))!.Questions
            .Select(aq => aq.Question).Single(q => q.Type == QuestionType.MultipleChoice);

        var result = await service.SubmitExamAsync(attempt.Token, new Dictionary<int, SubmittedAnswer>
        {
            [multi.Id] = SubmittedAnswer.Choices(new[] { multi.Options.Single(o => o.Text == "3").Id })
        });

        var questionResult = result.Value!.Questions.Single(r => r.Question.Id == multi.Id);
        Assert.Equal(2m, questionResult.AwardedPoints);
        Assert.True(questionResult.IsPartial);
        Assert.False(questionResult.IsCorrect);
        Assert.Equal(2m, result.Value.Score);
    }

    // ---------- The question form ----------

    [Fact]
    public void Form_MultipleChoice_DropsEmptyRowsAndRemapsTheTicks()
    {
        var form = new QuestionFormViewModel
        {
            Type = QuestionType.MultipleChoice,
            Text = "Кои?",
            Options = new List<string> { "а", "", "б", "в" },
            CorrectIndexes = new List<int> { 0, 1, 3 },   // row 1 is empty and is dropped
            CorrectIndex = 2,
            Points = 3
        };

        var input = form.ToInput();

        Assert.Equal(QuestionType.MultipleChoice, input.Type);
        Assert.Equal(new[] { "а", "б", "в" }, input.Options);
        Assert.Equal(new[] { 50m, -50m, 50m }, input.Fractions);
        Assert.Equal(3, input.Points);
    }

    [Fact]
    public void Form_SingleChoice_IgnoresTheTicks()
    {
        var form = new QuestionFormViewModel
        {
            Text = "В",
            Options = new List<string> { "а", "б" },
            CorrectIndex = 1,
            CorrectIndexes = new List<int> { 0 }
        };

        var input = form.ToInput();

        Assert.Equal(QuestionType.SingleChoice, input.Type);
        Assert.Null(input.Fractions);
        Assert.Equal(1, input.CorrectIndex);
    }

    [Fact]
    public void Form_FromMultipleChoiceQuestion_FillsTheTicks()
    {
        var question = Question(50m, -50m, 50m);

        var form = QuestionFormViewModel.FromQuestion(question);

        Assert.Equal(QuestionType.MultipleChoice, form.Type);
        Assert.Equal(new[] { "о1", "о2", "о3" }, form.Options);
        Assert.Equal(new[] { 0, 2 }, form.CorrectIndexes);
        Assert.False(form.HasCustomShares);
    }

    [Fact]
    public void Form_FromImportedShares_WarnsThatSavingReplacesThem()
    {
        // Moodle-style shares: no penalty for the wrong option
        var form = QuestionFormViewModel.FromQuestion(Question(50m, 50m, 0m));

        Assert.Equal(new[] { 0, 1 }, form.CorrectIndexes);
        Assert.True(form.HasCustomShares);
    }

    // ---------- Showing the right answers ----------

    [Theory]
    [InlineData(100, true)]
    [InlineData(33.33, true)]
    [InlineData(0, false)]
    [InlineData(-50, false)]
    public void Credits_AnyPositiveShare(double fraction, bool expected)
    {
        Assert.Equal(expected, QuestionResult.Credits(new AnswerOption { Fraction = (decimal)fraction }));
    }
}
