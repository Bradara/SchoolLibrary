using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>Pictures in the question text of a Moodle XML file: parsing and storing on import.</summary>
public class MoodleImageImportTests
{
    private const string Owner = "teacher-1";

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 5, 6 };
    private static readonly string PngBase64 = Convert.ToBase64String(Png);

    private static string Quiz(string questions) => $"<?xml version=\"1.0\"?><quiz>{questions}</quiz>";

    private static string File(string name, string base64, string path = "/") =>
        $"<file name=\"{name}\" path=\"{path}\" encoding=\"base64\">{base64}</file>";

    // A single-choice question with the given question-text HTML and files
    private static string Question(string html, string files = "", string type = "multichoice") =>
        $"<question type=\"{type}\"><name><text>Фигура</text></name><single>true</single>" +
        $"<questiontext format=\"html\"><text><![CDATA[{html}]]></text>{files}</questiontext>" +
        "<answer fraction=\"100\"><text>Да</text></answer><answer fraction=\"0\"><text>Не</text></answer></question>";

    // ---------- parsing ----------

    [Fact]
    public void Parse_PluginFileImage_BecomesMarkerWithItsBytes()
    {
        var xml = Quiz(Question(
            "<p>Разгледайте:</p><p><img src=\"@@PLUGINFILE@@/image.png\" alt=\"Стек с 3 елемента\" width=\"200\"></p><p>Кое е вярно?</p>",
            File("image.png", PngBase64)));

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Errors);
        var q = Assert.Single(result.Questions);
        Assert.Equal("Разгледайте:\n[[img:1]]\nКое е вярно?", q.Text);   // each paragraph on its own line
        var image = Assert.Single(q.Images!);
        Assert.Equal(1, image.Number);
        Assert.Equal("image.png", image.FileName);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(Png, image.Bytes);
        Assert.Equal("Стек с 3 елемента", image.AltText);
    }

    [Fact]
    public void Parse_QuestionWithoutImages_HasNoImageList()
    {
        var result = MoodleXmlParser.Parse(Quiz(Question("<p>Без снимка</p>")));

        Assert.Null(Assert.Single(result.Questions).Images);
    }

    [Fact]
    public void Parse_TwoImagesAndRepeatedOne_NumbersInOrderOnce()
    {
        var xml = Quiz(Question(
            "<img src=\"@@PLUGINFILE@@/a.png\"> и <img src='@@PLUGINFILE@@/b%20c.jpg'> пак <img src=\"@@PLUGINFILE@@/a.png\">",
            File("a.png", PngBase64) + File("b c.jpg", Convert.ToBase64String(Jpeg))));

        var q = Assert.Single(MoodleXmlParser.Parse(xml).Questions);

        Assert.Equal("[[img:1]] и [[img:2]] пак [[img:1]]", q.Text);
        Assert.Equal(new[] { "a.png", "b c.jpg" }, q.Images!.Select(i => i.FileName));
        Assert.Equal("image/jpeg", q.Images![1].ContentType);
    }

    [Fact]
    public void Parse_FileInSubfolder_IsMatchedByPath()
    {
        var xml = Quiz(Question("<img src=\"@@PLUGINFILE@@/pics/x.png\">", File("x.png", PngBase64, "/pics/")));

        var q = Assert.Single(MoodleXmlParser.Parse(xml).Questions);

        Assert.Single(q.Images!);
    }

    [Fact]
    public void Parse_DataAddress_IsRead()
    {
        var xml = Quiz(Question($"<img src=\"data:image/png;base64,{PngBase64}\" alt=\"\">"));

        var q = Assert.Single(MoodleXmlParser.Parse(xml).Questions);

        var image = Assert.Single(q.Images!);
        Assert.Equal(Png, image.Bytes);
        Assert.Null(image.AltText);
    }

    [Fact]
    public void Parse_ImageFromAnotherSite_IsSkippedWithReason_AndNotFetched()
    {
        var xml = Quiz(Question("<p>Текст <img src=\"https://example.com/x.png\"></p>"));

        var result = MoodleXmlParser.Parse(xml);

        var q = Assert.Single(result.Questions);
        Assert.Equal("Текст", q.Text);
        Assert.Null(q.Images);
        Assert.Contains(result.Errors, e => e.Contains("други сайтове"));
    }

    [Fact]
    public void Parse_MissingFile_IsSkippedWithReason()
    {
        var result = MoodleXmlParser.Parse(Quiz(Question("Текст <img src=\"@@PLUGINFILE@@/none.png\">")));

        Assert.Equal("Текст", Assert.Single(result.Questions).Text);
        Assert.Contains(result.Errors, e => e.Contains("none.png") && e.Contains("липсва"));
    }

    [Fact]
    public void Parse_BrokenBase64_IsSkipped()
    {
        var result = MoodleXmlParser.Parse(Quiz(Question("Текст <img src=\"@@PLUGINFILE@@/a.png\">", File("a.png", "%%%не е base64"))));

        Assert.Null(Assert.Single(result.Questions).Images);
        Assert.Contains(result.Errors, e => e.Contains("повреден"));
    }

    [Fact]
    public void Parse_SvgFile_IsSkipped()
    {
        var svg = Convert.ToBase64String("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray());

        var result = MoodleXmlParser.Parse(Quiz(Question("Текст <img src=\"@@PLUGINFILE@@/a.svg\">", File("a.svg", svg))));

        Assert.Null(Assert.Single(result.Questions).Images);
        Assert.Contains(result.Errors, e => e.Contains("PNG, JPEG, GIF и WebP"));
    }

    [Fact]
    public void Parse_TooLargeFile_IsSkipped()
    {
        var big = new byte[QuestionImage.MaxSizeBytes + 1];
        Png.CopyTo(big, 0);

        var result = MoodleXmlParser.Parse(Quiz(Question("Текст <img src=\"@@PLUGINFILE@@/a.png\">", File("a.png", Convert.ToBase64String(big)))));

        Assert.Null(Assert.Single(result.Questions).Images);
        Assert.Contains(result.Errors, e => e.Contains("МБ"));
    }

    [Fact]
    public void Parse_EleventhImage_IsSkipped()
    {
        var html = string.Concat(Enumerable.Range(1, 11).Select(i => $"<img src=\"@@PLUGINFILE@@/{i}.png\">"));
        var files = string.Concat(Enumerable.Range(1, 11).Select(i => File($"{i}.png", PngBase64)));

        var result = MoodleXmlParser.Parse(Quiz(Question(html, files)));

        Assert.Equal(QuestionImage.MaxPerQuestion, Assert.Single(result.Questions).Images!.Count);
        Assert.Contains(result.Errors, e => e.Contains("11.png"));
    }

    [Fact]
    public void Parse_OnlyAnImage_IsStillAQuestion()
    {
        var result = MoodleXmlParser.Parse(Quiz(Question("<img src=\"@@PLUGINFILE@@/a.png\">", File("a.png", PngBase64))));

        Assert.Equal("[[img:1]]", Assert.Single(result.Questions).Text);
    }

    [Fact]
    public void Parse_CodeRunnerStatement_KeepsMarkerOnItsLine()
    {
        var xml = Quiz(
            "<question type=\"coderunner\"><name><text>Сума</text></name>" +
            $"<questiontext format=\"html\"><text><![CDATA[<p>Прочетете две числа.</p><p><img src=\"@@PLUGINFILE@@/a.png\"></p><p>Отпечатайте сумата.</p>]]></text>{File("a.png", PngBase64)}</questiontext>" +
            "<coderunnertype>python3</coderunnertype>" +
            "<testcases><testcase testtype=\"0\" useasexample=\"1\" mark=\"1.0\"><stdin><text>1 2</text></stdin><expected><text>3</text></expected></testcase></testcases>" +
            "</question>");

        var q = Assert.Single(MoodleXmlParser.Parse(xml).Questions);

        Assert.Equal("Сума", q.Text);
        Assert.Equal("Прочетете две числа.\n[[img:1]]\nОтпечатайте сумата.", q.Code!.Statement);
        Assert.Single(q.Images!);
    }

    // ---------- import into the bank ----------

    private static Section NewSection(ApplicationDbContext context)
    {
        var section = new Section { Name = "Внос", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        context.SaveChanges();
        return section;
    }

    [Fact]
    public async Task Import_StoresPicturesAndRows()
    {
        using var context = TestDbContextFactory.Create();
        var storage = new InMemoryFileStorage();
        var section = NewSection(context);
        var xml = Quiz(Question("Виж <img src=\"@@PLUGINFILE@@/a.png\" alt=\"Опашка\">", File("a.png", PngBase64)));

        var result = await new QuestionBankService(context, storage).ImportAsync(section.Id, xml, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.Added);
        var question = context.Questions.Single();
        Assert.Equal("Виж [[img:1]]", question.Text);
        var image = context.QuestionImages.Single();
        Assert.Equal(question.Id, image.QuestionId);
        Assert.Equal(1, image.Number);
        Assert.Equal("Опашка", image.AltText);
        Assert.Equal(Png.Length, image.SizeBytes);
        Assert.Equal(Png, storage.Files[image.StorageKey]);
        Assert.StartsWith(QuestionImageFiles.KeyPrefix, image.StorageKey);
    }

    [Fact]
    public async Task Import_InvalidQuestion_StoresNoPicture()
    {
        using var context = TestDbContextFactory.Create();
        var storage = new InMemoryFileStorage();
        var section = NewSection(context);
        // No correct answer: the question is refused by the parser, so its picture is never stored
        var xml = Quiz("<question type=\"multichoice\"><single>true</single>" +
                       $"<questiontext><text><![CDATA[<img src=\"@@PLUGINFILE@@/a.png\">]]></text>{File("a.png", PngBase64)}</questiontext>" +
                       "<answer fraction=\"0\"><text>А</text></answer><answer fraction=\"0\"><text>Б</text></answer></question>");

        await new QuestionBankService(context, storage).ImportAsync(section.Id, xml, Owner, false);

        Assert.Empty(storage.Files);
        Assert.Empty(context.QuestionImages);
    }

    [Fact]
    public async Task Import_WithoutStorage_RemovesMarkersAndReports()
    {
        using var context = TestDbContextFactory.Create();
        var section = NewSection(context);
        var xml = Quiz(Question("Виж <img src=\"@@PLUGINFILE@@/a.png\"> тук", File("a.png", PngBase64)));

        var result = await new QuestionBankService(context).ImportAsync(section.Id, xml, Owner, false);

        Assert.Equal("Виж  тук", context.Questions.Single().Text);
        Assert.Empty(context.QuestionImages);
        Assert.Contains(result.Value!.Errors, e => e.Contains("хранилище"));
    }
}
