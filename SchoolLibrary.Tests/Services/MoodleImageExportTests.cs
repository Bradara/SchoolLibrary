using System.Xml.Linq;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>Pictures of a question text in the Moodle XML export, and back through the import.</summary>
public class MoodleImageExportTests
{
    private const string Owner = "teacher-1";

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 8, 7 };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2 };

    private static QuestionImage Image(int number, string key, string fileName = "image.png", string? alt = null) => new()
    {
        Number = number, StorageKey = key, FileName = fileName, ContentType = "image/png", SizeBytes = 8, AltText = alt
    };

    private static Question SingleChoice(string text, params QuestionImage[] images) => new()
    {
        Text = text,
        Type = QuestionType.SingleChoice,
        Points = 1,
        Options = { new AnswerOption { Text = "Да", Fraction = 100, Order = 0 }, new AnswerOption { Text = "Не", Order = 1 } },
        Images = images.ToList()
    };

    private static XElement QuestionText(string xml) =>
        XDocument.Parse(xml).Descendants("question").First(q => (string?)q.Attribute("type") != "category").Element("questiontext")!;

    [Fact]
    public void Write_MarkerBecomesPluginFileImage_WithBase64File()
    {
        var question = SingleChoice("Виж [[img:1]] и отговори", Image(1, "questions/a.png", "стек 1.png", "Стек \"А\""));

        var xml = MoodleXmlWriter.Write(new[] { question }, new Dictionary<string, byte[]> { ["questions/a.png"] = Png });

        var text = QuestionText(xml);
        Assert.Equal("<p>Виж <img src=\"@@PLUGINFILE@@/%D1%81%D1%82%D0%B5%D0%BA%201.png\" alt=\"Стек &quot;А&quot;\"> и отговори</p>",
            text.Element("text")!.Value);
        var file = Assert.Single(text.Elements("file"));
        Assert.Equal("стек 1.png", (string?)file.Attribute("name"));
        Assert.Equal("/", (string?)file.Attribute("path"));
        Assert.Equal("base64", (string?)file.Attribute("encoding"));
        Assert.Equal(Png, Convert.FromBase64String(file.Value));
    }

    [Fact]
    public void Write_UnplacedPicture_GoesAtTheEnd()
    {
        var question = SingleChoice("Текст", Image(1, "questions/a.png"));

        var xml = MoodleXmlWriter.Write(new[] { question }, new Dictionary<string, byte[]> { ["questions/a.png"] = Png });

        Assert.Equal("<p>Текст<br><img src=\"@@PLUGINFILE@@/image.png\" alt=\"\"></p>", QuestionText(xml).Element("text")!.Value);
    }

    [Fact]
    public void Write_SameFileNameTwice_GetsUniqueNames()
    {
        var question = SingleChoice("[[img:1]] [[img:2]]", Image(1, "questions/a.png"), Image(2, "questions/b.png"));

        var xml = MoodleXmlWriter.Write(new[] { question },
            new Dictionary<string, byte[]> { ["questions/a.png"] = Png, ["questions/b.png"] = Jpeg });

        var names = QuestionText(xml).Elements("file").Select(f => (string?)f.Attribute("name")).ToList();
        Assert.Equal(new[] { "image.png", "2-image.png" }, names);
        Assert.Contains("@@PLUGINFILE@@/2-image.png", QuestionText(xml).Element("text")!.Value);
    }

    [Fact]
    public void Write_PictureWithoutBytes_IsLeftOut_AndNameHasNoMarker()
    {
        var question = SingleChoice("Виж [[img:1]]", Image(1, "questions/missing.png"));

        var xml = MoodleXmlWriter.Write(new[] { question });

        Assert.Equal("<p>Виж </p>", QuestionText(xml).Element("text")!.Value);
        Assert.Empty(QuestionText(xml).Elements("file"));
        Assert.Equal("Виж [изображение]", XDocument.Parse(xml).Descendants("question").First().Element("name")!.Element("text")!.Value);
    }

    [Fact]
    public void Write_TextIsStillEncoded()
    {
        var question = SingleChoice("<script>x</script> [[img:1]]", Image(1, "questions/a.png", "a\"><script>.png"));

        var xml = MoodleXmlWriter.Write(new[] { question }, new Dictionary<string, byte[]> { ["questions/a.png"] = Png });

        var html = QuestionText(xml).Element("text")!.Value;
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Write_CodeQuestion_PutsPicturesInTheTask()
    {
        var question = new Question
        {
            Text = "Сума", Type = QuestionType.Code, CodeLanguage = "python3", Statement = "Прочетете:\n[[img:1]]",
            TestCases = { new CodeTestCase { Stdin = "1 2", Expected = "3" } },
            Images = { Image(1, "questions/a.png") }
        };

        var xml = MoodleXmlWriter.Write(new[] { question }, new Dictionary<string, byte[]> { ["questions/a.png"] = Png });

        Assert.Equal("<p>Прочетете:<br><img src=\"@@PLUGINFILE@@/image.png\" alt=\"\"></p>", QuestionText(xml).Element("text")!.Value);
        Assert.Single(QuestionText(xml).Elements("file"));
    }

    [Fact]
    public void Write_ThenParse_GivesTheSamePictures()
    {
        var question = SingleChoice("Виж [[img:1]] и [[img:2]]",
            Image(1, "questions/a.png", "a.png", "Първа"), Image(2, "questions/b.jpg", "b.jpg"));

        var xml = MoodleXmlWriter.Write(new[] { question },
            new Dictionary<string, byte[]> { ["questions/a.png"] = Png, ["questions/b.jpg"] = Jpeg });
        var parsed = Assert.Single(MoodleXmlParser.Parse(xml).Questions);

        Assert.Equal("Виж [[img:1]] и [[img:2]]", parsed.Text);
        Assert.Equal(new[] { Png, Jpeg }, parsed.Images!.Select(i => i.Bytes));
        Assert.Equal("Първа", parsed.Images![0].AltText);
        Assert.Null(parsed.Images[1].AltText);
    }

    [Fact]
    public async Task ExportSection_ReadsPictureBytesFromStorage()
    {
        using var context = TestDbContextFactory.Create();
        var storage = new InMemoryFileStorage();
        storage.Files["questions/a.png"] = Png;
        var section = new Section { Name = "Стек", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        var question = SingleChoice("Виж [[img:1]]", Image(1, "questions/a.png"));
        question.Section = section;
        context.Questions.Add(question);
        context.SaveChanges();

        var export = (await new QuestionExportService(context, storage).ExportSectionAsync(section.Id, Owner, false)).Value!;

        var file = Assert.Single(QuestionText(export.Content).Elements("file"));
        Assert.Equal(Png, Convert.FromBase64String(file.Value));
    }

    [Fact]
    public async Task ExportQuiz_MissingStoredFile_IsLeftOut()
    {
        using var context = TestDbContextFactory.Create();
        var quiz = new Quiz { Title = "Тест", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        quiz.Questions.Add(SingleChoice("Виж [[img:1]]", Image(1, "questions/gone.png")));
        context.Quizzes.Add(quiz);
        context.SaveChanges();

        var export = (await new QuestionExportService(context, new InMemoryFileStorage()).ExportQuizAsync(quiz.Id, Owner, false)).Value!;

        Assert.Empty(QuestionText(export.Content).Elements("file"));
        Assert.DoesNotContain("<img", QuestionText(export.Content).Element("text")!.Value);
    }
}
