using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Services;

/// <summary>True/false questions end to end: input, validation, storage, grading, shuffling, import and the form.</summary>
public class TrueFalseQuestionTests
{
    private const string Owner = "teacher-1";

    private static readonly string[] Fixed = { "Вярно", "Невярно" };

    // ---------- QuestionInput.TrueFalse ----------

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(null, -1)]
    public void Input_TrueFalse_HasTheFixedOptionsAndMarksTheAnswer(bool? answer, int expectedIndex)
    {
        var input = QuestionInput.TrueFalse("Слънцето е звезда.", answer, 3);

        Assert.Equal(QuestionType.TrueFalse, input.Type);
        Assert.Equal(Fixed, input.Options);
        Assert.Equal(expectedIndex, input.CorrectIndex);
        Assert.Equal(3, input.Points);
    }

    // ---------- Validation ----------

    [Fact]
    public void Validate_TrueFalse_IsValid()
    {
        Assert.Null(QuizService.ValidateQuestion(QuestionInput.TrueFalse("Твърдение", false)));
    }

    [Fact]
    public void Validate_TrueFalseWithoutAnAnswer_AsksForTheCorrectOne()
    {
        Assert.Equal("Посочете кой е верният отговор.", QuizService.ValidateQuestion(QuestionInput.TrueFalse("Твърдение", null)));
    }

    [Fact]
    public void Validate_TrueFalseWithOtherOptions_IsRejected()
    {
        var input = new QuestionInput("Твърдение", new[] { "Да", "Не" }, 0, 1, QuestionType.TrueFalse);

        Assert.Equal("Въпросът „Вярно/Невярно“ има точно два отговора: „Вярно“ и „Невярно“.", QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_UnknownType_IsRejected()
    {
        var input = new QuestionInput("Въпрос", new[] { "а", "б" }, 0, 1, (QuestionType)99);

        Assert.Equal("Неподдържан вид въпрос.", QuizService.ValidateQuestion(input));
    }

    // ---------- Storage ----------

    [Fact]
    public async Task AddQuestionAsync_StoresATrueFalseQuestion()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Упражнение", null, QuizMode.Practice, null, null), Owner)).Value!;

        var result = await service.AddQuestionAsync(quiz.Id, QuestionInput.TrueFalse("  Земята е плоска.  ", false, 2), Owner, false);

        Assert.True(result.Succeeded);
        var saved = (await service.GetByIdAsync(quiz.Id))!.Questions.Single();
        Assert.Equal(QuestionType.TrueFalse, saved.Type);
        Assert.Equal("Земята е плоска.", saved.Text);
        Assert.Equal(2, saved.Points);
        Assert.Equal(Fixed, saved.Options.OrderBy(o => o.Order).Select(o => o.Text));
        Assert.Equal(new[] { false, true }, saved.Options.OrderBy(o => o.Order).Select(o => o.IsCorrect));
    }

    [Fact]
    public async Task UpdateQuestionAsync_CanTurnAQuestionIntoTrueFalseAndBack()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Упражнение", null, QuizMode.Practice, null, null), Owner)).Value!;
        var question = (await service.AddQuestionAsync(quiz.Id, new QuestionInput("2 + 2?", new[] { "4", "5", "6" }, 0), Owner, false)).Value!;

        var toTrueFalse = await service.UpdateQuestionAsync(question.Id, QuestionInput.TrueFalse("2 + 2 = 4", true), Owner, false);
        Assert.True(toTrueFalse.Succeeded);
        var saved = (await service.GetQuestionAsync(question.Id))!;
        Assert.Equal(QuestionType.TrueFalse, saved.Type);
        Assert.Equal(Fixed, saved.Options.Select(o => o.Text));

        var back = await service.UpdateQuestionAsync(question.Id, new QuestionInput("2 + 2?", new[] { "3", "4" }, 1), Owner, false);
        Assert.True(back.Succeeded);
        saved = (await service.GetQuestionAsync(question.Id))!;
        Assert.Equal(QuestionType.SingleChoice, saved.Type);
        Assert.Equal(new[] { "3", "4" }, saved.Options.Select(o => o.Text));
    }

    [Fact]
    public async Task BankImport_StoresMoodleTrueFalseAsTrueFalse()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuestionBankService(context);
        var section = new Section { Name = "Природа", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();

        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"truefalse\">" +
                  "<questiontext><text>Водата кипи при 100 °C.</text></questiontext>" +
                  "<answer fraction=\"100\"><text>true</text></answer>" +
                  "<answer fraction=\"0\"><text>false</text></answer></question></quiz>";
        var result = await service.ImportAsync(section.Id, xml, Owner, false);

        Assert.Equal(1, result.Value!.Added);
        var saved = Assert.Single(await service.GetBySectionAsync(section.Id));
        Assert.Equal(QuestionType.TrueFalse, saved.Type);
        Assert.True(saved.Options.OrderBy(o => o.Order).First().IsCorrect);
    }

    // ---------- Grading ----------

    private static Question Stored(bool answer) => new()
    {
        Id = 1,
        Type = QuestionType.TrueFalse,
        Points = 2,
        Options = new List<AnswerOption>
        {
            new() { Id = 10, Text = "Вярно", IsCorrect = answer, Order = 1 },
            new() { Id = 11, Text = "Невярно", IsCorrect = !answer, Order = 2 }
        }
    };

    [Fact]
    public void Grader_TrueFalse_IsFound()
    {
        Assert.IsType<TrueFalseGrader>(QuestionGraders.For(QuestionType.TrueFalse));
    }

    [Theory]
    [InlineData(true, 10, 2, true)]
    [InlineData(true, 11, 0, false)]
    [InlineData(false, 11, 2, true)]
    [InlineData(false, 10, 0, false)]
    public void Grader_TrueFalse_GradesThePick(bool answer, int picked, int expectedPoints, bool expectedCorrect)
    {
        var grade = new TrueFalseGrader().Grade(Stored(answer), SubmittedAnswer.Choice(picked));

        Assert.Equal(expectedPoints, grade.AwardedPoints);
        Assert.Equal(expectedCorrect, grade.IsCorrect);
    }

    [Fact]
    public void Grader_TrueFalse_OptionOfAnotherQuestion_IsUnanswered()
    {
        var grade = new TrueFalseGrader().Grade(Stored(true), SubmittedAnswer.Choice(99));

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.Same(SubmittedAnswer.Empty, grade.Answer);
    }

    // ---------- Shuffling ----------

    [Fact]
    public void Shuffle_KeepsTheTrueFalseOrder()
    {
        // Every seed must leave "Вярно" first
        for (var seed = 0; seed < 50; seed++)
        {
            var question = Stored(true);
            QuizShuffle.ShuffleOptions(question, new Random(seed));
            Assert.Equal(new[] { 10, 11 }, question.Options.Select(o => o.Id));
        }
    }

    [Fact]
    public void ShuffleForAttempt_KeepsTheTrueFalseOrder()
    {
        for (var i = 0; i < 20; i++)
        {
            var question = Stored(false);
            QuizShuffle.ShuffleOptionsForAttempt(question, Guid.NewGuid());
            Assert.Equal(new[] { 10, 11 }, question.Options.Select(o => o.Id));
        }
    }

    // ---------- The question form ----------

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void Form_TrueFalse_MapsToTheFixedOptions(bool answer, int expectedIndex)
    {
        // Leftover single-choice rows in the hidden panel are ignored
        var form = new QuestionFormViewModel
        {
            Type = QuestionType.TrueFalse,
            Text = "Твърдение",
            TrueFalseAnswer = answer,
            Options = new List<string> { "стар", "отговор" },
            CorrectIndex = 1,
            Points = 2
        };

        var input = form.ToInput();

        Assert.Equal(QuestionType.TrueFalse, input.Type);
        Assert.Equal(Fixed, input.Options);
        Assert.Equal(expectedIndex, input.CorrectIndex);
        Assert.Equal(2, input.Points);
    }

    [Fact]
    public void Form_SingleChoice_KeepsItsType()
    {
        var form = new QuestionFormViewModel { Text = "В", Options = new List<string> { "а", "", "б" }, CorrectIndex = 2, TrueFalseAnswer = true };

        var input = form.ToInput();

        Assert.Equal(QuestionType.SingleChoice, input.Type);
        Assert.Equal(new[] { "а", "б" }, input.Options);
        Assert.Equal(1, input.CorrectIndex);
    }

    [Fact]
    public void Form_UnknownType_IsPassedOnForTheServiceToReject()
    {
        var form = new QuestionFormViewModel { Type = (QuestionType)99, Text = "В", Options = new List<string> { "а", "б" }, CorrectIndex = 0 };

        Assert.Equal("Неподдържан вид въпрос.", QuizService.ValidateQuestion(form.ToInput()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Form_FromTrueFalseQuestion_FillsTheAnswer(bool answer)
    {
        var form = QuestionFormViewModel.FromQuestion(Stored(answer));

        Assert.Equal(QuestionType.TrueFalse, form.Type);
        Assert.Equal(answer, form.TrueFalseAnswer);
        Assert.Empty(form.Options);
        Assert.Equal(-1, form.CorrectIndex);
    }

    [Fact]
    public void Form_FromSingleChoiceQuestion_FillsTheOptions()
    {
        var question = new Question
        {
            Text = "В",
            Points = 3,
            Options = new List<AnswerOption>
            {
                new() { Text = "б", Order = 2, IsCorrect = true },
                new() { Text = "а", Order = 1 }
            }
        };

        var form = QuestionFormViewModel.FromQuestion(question);

        Assert.Equal(QuestionType.SingleChoice, form.Type);
        Assert.Equal(new[] { "а", "б" }, form.Options);
        Assert.Equal(1, form.CorrectIndex);
        Assert.Null(form.TrueFalseAnswer);
        Assert.Equal(3, form.Points);
    }
}
