using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Services;

/// <summary>Descriptions: text between the questions, without options or points, never drawn at random.</summary>
public class DescriptionQuestionTests
{
    private const string Owner = "teacher-1";

    // ---------- Input and validation ----------

    [Fact]
    public void Input_Description_HasNoOptionsAndNoPoints()
    {
        var input = QuestionInput.Description("Прочетете текста и отговорете на въпросите.");

        Assert.Equal(QuestionType.Description, input.Type);
        Assert.Empty(input.Options);
        Assert.Equal(0, input.Points);
        Assert.Null(QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_DescriptionWithoutText_IsRejected()
    {
        Assert.Equal("Текстът на въпроса е задължителен.", QuizService.ValidateQuestion(QuestionInput.Description(" ")));
    }

    [Fact]
    public void Validate_TooLongDescription_IsRejected()
    {
        Assert.NotNull(QuizService.ValidateQuestion(QuestionInput.Description(new string('а', 1001))));
    }

    // ---------- Storage ----------

    [Fact]
    public async Task AddQuestionAsync_StoresADescriptionWithoutOptionsOrPoints()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Упражнение", null, QuizMode.Practice, null, null), Owner)).Value!;

        // Even when options and points are sent, a description keeps neither
        var input = new QuestionInput("Указания", new[] { "а", "б" }, 0, 5, QuestionType.Description);
        var result = await service.AddQuestionAsync(quiz.Id, input, Owner, false);

        Assert.True(result.Succeeded);
        var saved = (await service.GetByIdAsync(quiz.Id))!.Questions.Single();
        Assert.Equal(QuestionType.Description, saved.Type);
        Assert.Equal(0, saved.Points);
        Assert.Empty(saved.Options);
    }

    [Fact]
    public async Task QuestionCount_LeavesOutDescriptions()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Упражнение", null, QuizMode.Practice, null, null), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id, QuestionInput.Description("Указания"), Owner, false);
        await service.AddQuestionAsync(quiz.Id, new QuestionInput("2 + 2?", new[] { "4", "5" }, 0), Owner, false);

        var listed = (await service.GetManagedAsync(Owner, 1)).Items.Single();

        Assert.Equal(1, listed.QuestionCount);
    }

    [Fact]
    public async Task RandomDraws_NeverPickADescription()
    {
        using var context = TestDbContextFactory.Create();
        var section = new Section { Name = "Четене", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();
        context.Questions.Add(new Question { SectionId = section.Id, Text = "Откъс", Type = QuestionType.Description, Order = 1 });
        context.Questions.Add(new Question
        {
            SectionId = section.Id, Text = "Въпрос", Points = 1, Order = 2,
            Options = new List<AnswerOption> { new() { Text = "а", IsCorrect = true, Order = 1 }, new() { Text = "б", Order = 2 } }
        });
        var quiz = new Quiz { Title = "Тест", OwnerId = Owner };
        context.Quizzes.Add(quiz);
        await context.SaveChangesAsync();

        var items = new QuizItemService(context);
        await items.AddRandomSectionAsync(quiz.Id, section.Id, 5, Owner, false);

        // Many draws: the description must never be one of them, and the pool counts only the question
        for (var seed = 0; seed < 20; seed++)
        {
            var drawn = await items.ResolveAsync(quiz.Id, new Random(seed));
            Assert.Equal("Въпрос", Assert.Single(drawn).Text);
        }

        var service = new QuizService(context, items);
        Assert.Equal(1, (await service.GetManagedAsync(Owner, 1)).Items.Single().QuestionCount);
    }

    // ---------- Grading ----------

    [Fact]
    public void Grader_IsRegistered_AndAwardsNothing()
    {
        var grader = QuestionGraders.For(QuestionType.Description);
        var grade = grader.Grade(new Question { Type = QuestionType.Description }, SubmittedAnswer.Choice(5));

        Assert.IsType<DescriptionGrader>(grader);
        Assert.Equal(0m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
        Assert.Same(SubmittedAnswer.Empty, grade.Answer);
    }

    [Fact]
    public void Grading_ADescription_DoesNotChangeTheScore()
    {
        var quiz = new Quiz
        {
            Questions = new List<Question>
            {
                new() { Id = 1, Type = QuestionType.Description, Text = "Указания", Order = 1 },
                new()
                {
                    Id = 2, Points = 2, Order = 2,
                    Options = new List<AnswerOption> { new() { Id = 20, IsCorrect = true }, new() { Id = 21 } }
                }
            }
        };

        var result = QuizGrading.Grade(quiz, new Dictionary<int, SubmittedAnswer> { [2] = SubmittedAnswer.Choice(20) });

        Assert.Equal(2m, result.Score);
        Assert.Equal(2m, result.MaxScore);
        Assert.Equal(6, result.Grade);
        Assert.True(result.Questions[0].IsDescription);
        Assert.False(result.Questions[1].IsDescription);
    }

    // ---------- Import ----------

    [Fact]
    public void Parse_Description_IsKeptAsText()
    {
        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"description\">" +
                  "<questiontext format=\"html\"><text><![CDATA[<p>Прочетете откъса.</p>]]></text></questiontext>" +
                  "</question></quiz>";

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Errors);
        var q = Assert.Single(result.Questions);
        Assert.Equal(QuestionType.Description, q.Type);
        Assert.Equal("Прочетете откъса.", q.Text);
        Assert.Null(QuizService.ValidateQuestion(q));
    }

    // ---------- The question form ----------

    [Fact]
    public void Form_Description_IgnoresOptionsAndPoints()
    {
        var form = new QuestionFormViewModel
        {
            Type = QuestionType.Description,
            Text = "Указания",
            Options = new List<string> { "а", "б" },
            CorrectIndex = 0,
            Points = 7
        };

        var input = form.ToInput();

        Assert.Equal(QuestionType.Description, input.Type);
        Assert.Empty(input.Options);
        Assert.Equal(0, input.Points);
    }

    [Fact]
    public void Form_FromDescription_KeepsAValidPointsValue()
    {
        // The points field is hidden but still validated ([Range(1, 100)]), so it must not be 0
        var form = QuestionFormViewModel.FromQuestion(new Question { Type = QuestionType.Description, Text = "Указания", Points = 0 });

        Assert.Equal(QuestionType.Description, form.Type);
        Assert.Equal("Указания", form.Text);
        Assert.Equal(1, form.Points);
    }
}
