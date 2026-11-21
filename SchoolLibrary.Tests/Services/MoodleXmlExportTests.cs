using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.Tests.Web;

namespace SchoolLibrary.Tests.Services;

/// <summary>Moodle XML export: every question type survives import → export → import; points; the service and the downloads.</summary>
public class MoodleXmlExportTests
{
    private const string Owner = "teacher-1";

    // Applies an input to a question the way the service stores it
    private static Question Stored(QuestionInput input, int id = 1)
    {
        var question = new Question { Id = id, Order = id };
        QuestionContent.Apply(question, input);
        var order = 0;
        foreach (var option in question.Options)
        {
            option.Order = ++order;
        }

        return question;
    }

    // Writes the questions and reads them back
    private static IReadOnlyList<QuestionInput> RoundTrip(params QuestionInput[] inputs)
    {
        var xml = MoodleXmlWriter.Write(inputs.Select((input, i) => Stored(input, i + 1)));
        var parsed = MoodleXmlParser.Parse(xml);
        Assert.Empty(parsed.Errors);
        return parsed.Questions;
    }

    // ---------- Round trip, per type ----------

    [Fact]
    public void RoundTrip_SingleChoice_KeepsTheCorrectOptionAndPoints()
    {
        var back = Assert.Single(RoundTrip(new QuestionInput("2 + 2 = ?", new[] { "3", "4", "5" }, 1, 3)));

        Assert.Equal(QuestionType.SingleChoice, back.Type);
        Assert.Equal("2 + 2 = ?", back.Text);
        Assert.Equal(new[] { "3", "4", "5" }, back.Options);
        Assert.Equal(1, back.CorrectIndex);
        Assert.Equal(3, back.Points);
    }

    [Fact]
    public void RoundTrip_TrueFalse_KeepsTheAnswer()
    {
        var results = RoundTrip(QuestionInput.TrueFalse("Земята е кръгла.", true, 2), QuestionInput.TrueFalse("Слънцето е планета.", false));

        Assert.All(results, q => Assert.Equal(QuestionType.TrueFalse, q.Type));
        Assert.Equal(0, results[0].CorrectIndex);
        Assert.Equal(2, results[0].Points);
        Assert.Equal(1, results[1].CorrectIndex);
    }

    [Fact]
    public void RoundTrip_MultipleChoice_KeepsTheShares()
    {
        var input = QuestionInput.MultipleChoice("Кои са прости?", new[] { "2", "3", "4", "5" }, new[] { 0, 1, 3 }, 4);

        var back = Assert.Single(RoundTrip(input));

        Assert.Equal(QuestionType.MultipleChoice, back.Type);
        Assert.Equal(input.Fractions, back.Fractions);
        Assert.Equal(4, back.Points);
    }

    [Fact]
    public void RoundTrip_ShortAnswer_KeepsAnswersSharesAndCase()
    {
        var input = new QuestionInput("Столицата на България?", new[] { "София", "Sofia", "Соф*" }, -1, 2,
            QuestionType.ShortAnswer, new[] { 100m, 100m, 50m }, CaseSensitive: true);

        var back = Assert.Single(RoundTrip(input));

        Assert.Equal(QuestionType.ShortAnswer, back.Type);
        Assert.Equal(input.Options, back.Options);
        Assert.Equal(input.Fractions, back.Fractions);
        Assert.True(back.CaseSensitive);
    }

    [Fact]
    public void RoundTrip_Numerical_KeepsValuesAndTolerances()
    {
        var input = QuestionInput.Numerical("π с точност 0,01?", new[] { "3,14", "*" }, new decimal?[] { 0.01m, null }, 5);

        var back = Assert.Single(RoundTrip(input));

        Assert.Equal(QuestionType.Numerical, back.Type);
        Assert.Equal(new[] { "3.14", "*" }, back.Options);
        Assert.Equal(new decimal?[] { 0.01m, 0m }, back.Tolerances);
        Assert.Equal(5, back.Points);
    }

    [Fact]
    public void RoundTrip_Matching_KeepsPairsAndDistractors()
    {
        var input = QuestionInput.Matching("Свържете столиците.",
            new[] { ("България", "София"), ("Франция", "Париж"), ("", "Берлин") });

        var back = Assert.Single(RoundTrip(input));

        Assert.Equal(QuestionType.Matching, back.Type);
        Assert.Equal(new[] { "България", "Франция", "" }, back.Options);
        Assert.Equal(new[] { "София", "Париж", "Берлин" }, back.Matches);
    }

    [Fact]
    public void RoundTrip_Description_StaysWithoutPoints()
    {
        var back = Assert.Single(RoundTrip(QuestionInput.Description("Прочетете откъса.")));

        Assert.Equal(QuestionType.Description, back.Type);
        Assert.Equal(0, back.Points);
        Assert.Empty(back.Options);
    }

    [Fact]
    public void RoundTrip_SpecialCharacters_AreKept()
    {
        var back = Assert.Single(RoundTrip(new QuestionInput("Кое е вярно: a < b & b > c?", new[] { "a < c", "\"c\" < a" }, 0)));

        Assert.Equal("Кое е вярно: a < b & b > c?", back.Text);
        Assert.Equal(new[] { "a < c", "\"c\" < a" }, back.Options);
    }

    [Fact]
    public void RoundTrip_EveryTypeTogether_KeepsTheOrder()
    {
        var results = RoundTrip(
            QuestionInput.Description("Указания"),
            new QuestionInput("Един избор", new[] { "а", "б" }, 0),
            QuestionInput.TrueFalse("Вярно ли е?", true),
            QuestionInput.MultipleChoice("Няколко", new[] { "а", "б", "в" }, new[] { 0, 1 }),
            QuestionInput.ShortAnswer("Кратък", new[] { "да" }),
            QuestionInput.Numerical("Число", new[] { "42" }, new decimal?[] { null }),
            QuestionInput.Matching("Двойки", new[] { ("1", "едно"), ("2", "две") }));

        Assert.Equal(new[]
        {
            QuestionType.Description, QuestionType.SingleChoice, QuestionType.TrueFalse, QuestionType.MultipleChoice,
            QuestionType.ShortAnswer, QuestionType.Numerical, QuestionType.Matching
        }, results.Select(q => q.Type));
        Assert.All(results, q => Assert.Null(QuizService.ValidateQuestion(q)));
    }

    // ---------- The written XML ----------

    [Fact]
    public void Write_SnapsSharesToMoodleGrades()
    {
        var xml = MoodleXmlWriter.Write(new[]
        {
            Stored(QuestionInput.MultipleChoice("Три верни", new[] { "а", "б", "в", "г" }, new[] { 0, 1, 2 }))
        });

        var fractions = XDocument.Parse(xml).Descendants("answer").Select(a => (string?)a.Attribute("fraction")).ToList();
        Assert.Equal(new[] { "33.33333", "33.33333", "33.33333", "-33.33333" }, fractions);
    }

    [Fact]
    public void Write_KeepsLineBreaksAsHtml_AndWritesTheGrade()
    {
        var xml = MoodleXmlWriter.Write(new[] { Stored(new QuestionInput("Ред 1\nРед 2", new[] { "а", "б" }, 0, 7)) });

        var question = XDocument.Parse(xml).Descendants("question").Single();
        Assert.Equal("<p>Ред 1<br>Ред 2</p>", question.Element("questiontext")!.Element("text")!.Value);
        Assert.Equal("7", question.Element("defaultgrade")!.Value);
        Assert.Equal("Ред 1 Ред 2", question.Element("name")!.Element("text")!.Value);
    }

    [Fact]
    public void Write_ShortensALongName()
    {
        var xml = MoodleXmlWriter.Write(new[] { Stored(new QuestionInput(new string('а', 100), new[] { "а", "б" }, 0)) });

        var name = XDocument.Parse(xml).Descendants("name").Single().Element("text")!.Value;
        Assert.Equal(new string('а', 60) + "…", name);
    }

    [Fact]
    public void Write_Groups_AddCategoryMarkersThatTheImportSkips()
    {
        var xml = MoodleXmlWriter.Write(new[]
        {
            new MoodleXmlWriter.Group(MoodleXmlWriter.CategoryPath(new[] { "Математика" }),
                new[] { Stored(new QuestionInput("1", new[] { "а", "б" }, 0)) }),
            new MoodleXmlWriter.Group(MoodleXmlWriter.CategoryPath(new[] { "Математика", "Дроби 1/2" }),
                new[] { Stored(new QuestionInput("2", new[] { "а", "б" }, 0)) })
        });

        var categories = XDocument.Parse(xml).Descendants("category").Select(c => c.Element("text")!.Value).ToList();
        Assert.Equal(new[] { "$course$/top/Математика", "$course$/top/Математика/Дроби 1//2" }, categories);
        Assert.Equal(2, MoodleXmlParser.Parse(xml).Questions.Count);
    }

    // ---------- Points on import ----------

    [Theory]
    [InlineData("<defaultgrade>3.0000000</defaultgrade>", 3)]
    [InlineData("<defaultgrade>2.5</defaultgrade>", 3)]
    [InlineData("<defaultgrade>0.2</defaultgrade>", 1)]
    [InlineData("<defaultgrade>500</defaultgrade>", 100)]
    [InlineData("<defaultgrade>много</defaultgrade>", 1)]
    [InlineData("", 1)]
    public void Parse_ReadsDefaultGradeAsPoints(string grade, int expected)
    {
        var xml = "<quiz><question type=\"multichoice\"><questiontext><text>Въпрос</text></questiontext>" + grade +
                  "<single>true</single><answer fraction=\"100\"><text>а</text></answer><answer fraction=\"0\"><text>б</text></answer>" +
                  "</question></quiz>";

        Assert.Equal(expected, Assert.Single(MoodleXmlParser.Parse(xml).Questions).Points);
    }

    // ---------- File name ----------

    [Theory]
    [InlineData("Дроби: 5. клас", "Дроби-5-клас.xml")]
    [InlineData("  ?!  ", "questions.xml")]
    [InlineData("a/b\\c", "a-b-c.xml")]
    public void FileName_KeepsLettersAndDigits(string title, string expected)
    {
        Assert.Equal(expected, QuestionExportService.FileName(title));
    }

    // ---------- The service ----------

    [Fact]
    public async Task ExportSection_WritesTheSubsectionsAsCategories()
    {
        using var context = TestDbContextFactory.Create();
        var root = new Section { Name = "Математика", OwnerId = Owner };
        context.Sections.Add(root);
        await context.SaveChangesAsync();
        var child = new Section { Name = "Дроби", OwnerId = Owner, ParentId = root.Id };
        var other = new Section { Name = "Чужд", OwnerId = "teacher-2" };
        context.Sections.AddRange(child, other);
        await context.SaveChangesAsync();
        context.Questions.AddRange(
            Question(root.Id, "Корен", 1),
            Question(child.Id, "Дроб", 1),
            Question(other.Id, "Чужд въпрос", 1));
        await context.SaveChangesAsync();

        var result = await new QuestionExportService(context).ExportSectionAsync(root.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal("Математика.xml", result.Value!.FileName);
        Assert.Equal(2, result.Value.QuestionCount);
        var document = XDocument.Parse(result.Value.Content);
        Assert.Equal(new[] { "$course$/top/Математика", "$course$/top/Математика/Дроби" },
            document.Descendants("category").Select(c => c.Element("text")!.Value));
        Assert.Equal(new[] { "Корен", "Дроб" }, MoodleXmlParser.Parse(result.Value.Content).Questions.Select(q => q.Text));
    }

    [Fact]
    public async Task ExportSection_MissingOrSomeoneElses_Fails()
    {
        using var context = TestDbContextFactory.Create();
        var section = new Section { Name = "Чужд", OwnerId = "teacher-2" };
        context.Sections.Add(section);
        await context.SaveChangesAsync();
        var service = new QuestionExportService(context);

        Assert.Equal(ServiceError.NotFound, (await service.ExportSectionAsync(999, Owner, false)).Error);
        Assert.Equal(ServiceError.Forbidden, (await service.ExportSectionAsync(section.Id, Owner, false)).Error);
        Assert.True((await service.ExportSectionAsync(section.Id, Owner, true)).Succeeded);
    }

    [Fact]
    public async Task ExportQuiz_WritesOwnThenPinnedQuestions_AndCountsRandomDraws()
    {
        using var context = TestDbContextFactory.Create();
        var section = new Section { Name = "Банка", OwnerId = Owner };
        var quiz = new Quiz { Title = "Тест 1", OwnerId = Owner };
        context.AddRange(section, quiz);
        await context.SaveChangesAsync();
        var pinned = Question(section.Id, "Закачен", 1);
        context.Questions.Add(pinned);
        var own = Question(null, "Пряк", 1);
        own.QuizId = quiz.Id;
        context.Questions.Add(own);
        await context.SaveChangesAsync();
        context.QuizItems.AddRange(
            new QuizItem { QuizId = quiz.Id, Order = 1, Kind = QuizItemKind.FixedQuestion, QuestionId = pinned.Id },
            new QuizItem { QuizId = quiz.Id, Order = 2, Kind = QuizItemKind.RandomFromSection, SectionId = section.Id, PickCount = 1 });
        await context.SaveChangesAsync();

        var result = await new QuestionExportService(context).ExportQuizAsync(quiz.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal("Тест-1.xml", result.Value!.FileName);
        Assert.Equal(2, result.Value.QuestionCount);
        Assert.Equal(1, result.Value.SkippedRandomItems);
        Assert.Equal(new[] { "Пряк", "Закачен" }, MoodleXmlParser.Parse(result.Value.Content).Questions.Select(q => q.Text));
    }

    [Fact]
    public async Task ExportQuiz_MissingOrSomeoneElses_Fails()
    {
        using var context = TestDbContextFactory.Create();
        var quiz = new Quiz { Title = "Чужд", OwnerId = "teacher-2" };
        context.Quizzes.Add(quiz);
        await context.SaveChangesAsync();
        var service = new QuestionExportService(context);

        Assert.Equal(ServiceError.NotFound, (await service.ExportQuizAsync(999, Owner, false)).Error);
        Assert.Equal(ServiceError.Forbidden, (await service.ExportQuizAsync(quiz.Id, Owner, false)).Error);
    }

    private static Question Question(int? sectionId, string text, int order) => new()
    {
        SectionId = sectionId, Text = text, Order = order, Points = 1,
        Options = new List<AnswerOption> { new() { Text = "а", IsCorrect = true, Order = 1 }, new() { Text = "б", Order = 2 } }
    };

    // ---------- The downloads ----------

    [Fact]
    public async Task SectionsExport_ReturnsAnXmlFile()
    {
        var export = new Mock<IQuestionExportService>();
        export.Setup(e => e.ExportSectionAsync(5, "t1", false))
            .ReturnsAsync(ServiceResult<QuestionExport>.Success(new QuestionExport("Тема.xml", "<quiz />", 0)));
        var controller = new SectionsController(Mock.Of<ISectionService>(), Mock.Of<IQuestionBankService>()).WithUser("t1", Roles.Teacher);

        var file = Assert.IsType<FileContentResult>(await controller.Export(5, export.Object));

        Assert.Equal("application/xml", file.ContentType);
        Assert.Equal("Тема.xml", file.FileDownloadName);
        Assert.Equal("<quiz />", System.Text.Encoding.UTF8.GetString(file.FileContents));
    }

    [Fact]
    public async Task QuizExport_MapsFailures()
    {
        var export = new Mock<IQuestionExportService>();
        export.Setup(e => e.ExportQuizAsync(1, "t1", false))
            .ReturnsAsync(ServiceResult<QuestionExport>.Failure(ServiceError.NotFound, "Няма"));
        export.Setup(e => e.ExportQuizAsync(2, "t1", false))
            .ReturnsAsync(ServiceResult<QuestionExport>.Failure(ServiceError.Forbidden, "Не"));
        export.Setup(e => e.ExportQuizAsync(3, "t1", false))
            .ReturnsAsync(ServiceResult<QuestionExport>.Success(new QuestionExport("Тест.xml", "<quiz />", 0)));
        var controller = new QuizManagementController(Mock.Of<IQuizService>(), Mock.Of<IQuizItemService>(),
            Mock.Of<ISectionService>(), Mock.Of<IQuestionBankService>()).WithUser("t1", Roles.Teacher);

        Assert.IsType<NotFoundResult>(await controller.Export(1, export.Object));
        Assert.IsType<ForbidResult>(await controller.Export(2, export.Object));
        Assert.Equal("Тест.xml", Assert.IsType<FileContentResult>(await controller.Export(3, export.Object)).FileDownloadName);
    }

    [Fact]
    public async Task SectionsExport_MapsFailures()
    {
        var export = new Mock<IQuestionExportService>();
        export.Setup(e => e.ExportSectionAsync(1, "t1", false))
            .ReturnsAsync(ServiceResult<QuestionExport>.Failure(ServiceError.NotFound, "Няма"));
        export.Setup(e => e.ExportSectionAsync(2, "t1", false))
            .ReturnsAsync(ServiceResult<QuestionExport>.Failure(ServiceError.Forbidden, "Не"));
        var controller = new SectionsController(Mock.Of<ISectionService>(), Mock.Of<IQuestionBankService>()).WithUser("t1", Roles.Teacher);

        Assert.IsType<NotFoundResult>(await controller.Export(1, export.Object));
        Assert.IsType<ForbidResult>(await controller.Export(2, export.Object));
    }
}
