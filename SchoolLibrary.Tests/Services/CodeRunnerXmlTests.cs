using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.CodeRunning;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>CodeRunner questions in Moodle XML: import of programs checked with stdin tests, and export.</summary>
public class CodeRunnerXmlTests
{
    private const string Owner = "teacher-1";

    // The shape Moodle exports: HTML text, CDATA code, a precheck-only test, HIDE / SHOW and marks
    private const string PythonQuestion = """
        <?xml version="1.0" encoding="UTF-8"?>
        <quiz>
          <question type="category"><category><text>$course$/top/Програмиране</text></category></question>
          <question type="coderunner">
            <name><text>Сбор</text></name>
            <questiontext format="html"><text><![CDATA[<p>Прочетете <b>две</b> числа и отпечатайте сбора им.</p><pre>Вход: 2 3
        Изход: 5</pre>]]></text></questiontext>
            <defaultgrade>3.0000000</defaultgrade>
            <coderunnertype>python3</coderunnertype>
            <prototypetype>0</prototypetype>
            <allornothing>0</allornothing>
            <answerpreload><![CDATA[a, b = map(int, input().split())
        ]]></answerpreload>
            <template></template>
            <testcases>
              <testcase testtype="0" useasexample="1" hiderestiffail="0" mark="1.0000000">
                <testcode><text></text></testcode>
                <stdin><text>2 3</text></stdin>
                <expected><text>5</text></expected>
                <extra><text></text></extra>
                <display><text>SHOW</text></display>
              </testcase>
              <testcase testtype="1" useasexample="0" hiderestiffail="0" mark="1.0000000">
                <testcode><text></text></testcode>
                <stdin><text>0 0</text></stdin>
                <expected><text>0</text></expected>
                <display><text>SHOW</text></display>
              </testcase>
              <testcase testtype="2" useasexample="0" hiderestiffail="0" mark="2.5000000">
                <testcode><text></text></testcode>
                <stdin><text>  -10   4
        </text></stdin>
                <expected><text>-6
        </text></expected>
                <display><text>HIDE</text></display>
              </testcase>
            </testcases>
          </question>
        </quiz>
        """;

    private static string CodeRunner(string inner, string type = "csharp_program", string prototype = "0")
        => $"""
            <quiz><question type="coderunner">
              <name><text>Q</text></name>
              <questiontext format="html"><text>Задача</text></questiontext>
              <coderunnertype>{type}</coderunnertype>
              <prototypetype>{prototype}</prototypetype>
              {inner}
            </question></quiz>
            """;

    private const string OneTest = """
        <testcases><testcase testtype="0" useasexample="0" mark="1.0000000">
          <testcode><text></text></testcode><stdin><text>1</text></stdin><expected><text>1</text></expected>
          <display><text>SHOW</text></display>
        </testcase></testcases>
        """;

    // ---------- Import ----------

    [Fact]
    public void Parse_PythonQuestion_ReadsTheSettingsAndTheTests()
    {
        var result = MoodleXmlParser.Parse(PythonQuestion);

        Assert.Empty(result.Errors);
        var input = Assert.Single(result.Questions);
        Assert.Equal(QuestionType.Code, input.Type);
        Assert.Equal(3, input.Points);
        Assert.Equal("Прочетете две числа и отпечатайте сбора им.\nВход: 2 3\nИзход: 5", input.Text);
        Assert.Equal(CodeLanguages.Python, input.Code!.Language);
        Assert.False(input.Code.AllOrNothing);
        Assert.StartsWith("a, b = map(int, input().split())", input.Code.Preload);
        Assert.Equal(
            new[]
            {
                new CodeTestInput("2 3", "5", IsExample: true, IsHidden: false, Weight: 1m),
                // The precheck-only test is left out; input and output are kept exactly as written
                new CodeTestInput("  -10   4\n", "-6\n", IsExample: false, IsHidden: true, Weight: 2.5m),
            },
            input.Code.Tests);
        Assert.Null(QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Parse_CSharpProgram_BecomesCSharp_AllOrNothingByDefault()
    {
        var input = Assert.Single(MoodleXmlParser.Parse(CodeRunner(OneTest)).Questions);

        Assert.Equal(CodeLanguages.CSharp, input.Code!.Language);
        Assert.True(input.Code.AllOrNothing);
        Assert.Null(input.Code.Preload);
        Assert.Equal(1, input.Points);
    }

    [Theory]
    [InlineData("HIDE_IF_FAIL", true)]
    [InlineData("HIDE_IF_SUCCEED", false)]
    [InlineData("show", false)]
    public void Parse_Display_HidesTheTestWhenMoodleWouldHideAFailure(string display, bool hidden)
    {
        var xml = CodeRunner(OneTest.Replace("<text>SHOW</text>", $"<text>{display}</text>"));

        Assert.Equal(hidden, Assert.Single(MoodleXmlParser.Parse(xml).Questions).Code!.Tests[0].IsHidden);
    }

    [Theory]
    [InlineData("c_function", "CodeRunner типът „c_function“ не се поддържа")]
    [InlineData("multilanguage_csharp", "не се поддържа")]
    public void Parse_UnsupportedCodeRunnerType_IsSkippedWithTheReason(string type, string reason)
    {
        var result = MoodleXmlParser.Parse(CodeRunner(OneTest, type));

        Assert.Empty(result.Questions);
        Assert.Contains(reason, Assert.Single(result.Errors));
    }

    [Fact]
    public void Parse_Prototype_IsSkipped()
    {
        var result = MoodleXmlParser.Parse(CodeRunner(OneTest + "<template>print(1)</template>", prototype: "2"));

        Assert.Contains("прототипите", Assert.Single(result.Errors));
    }

    [Fact]
    public void Parse_OwnTemplate_IsSkipped()
    {
        var result = MoodleXmlParser.Parse(CodeRunner(OneTest + "<template><![CDATA[{{ STUDENT_ANSWER }}]]></template>"));

        Assert.Contains("собствен шаблон", Assert.Single(result.Errors));
    }

    [Fact]
    public void Parse_TestWithTestCode_IsSkipped()
    {
        var result = MoodleXmlParser.Parse(CodeRunner(OneTest.Replace("<testcode><text></text></testcode>",
            "<testcode><text>print(sum_digits(123))</text></testcode>"), "python3"));

        Assert.Contains("testcode", Assert.Single(result.Errors));
    }

    [Fact]
    public void Parse_TheBuiltInPrototypesExport_SkipsEveryEntry()
    {
        // An export of the CR_PROTOTYPES category holds only prototypes (prototypetype 1 or 2)
        const string xml = """
            <quiz>
              <question type="category"><category><text>$system$/top/CR_PROTOTYPES</text></category></question>
              <question type="coderunner"><name><text>BUILT_IN_PROTOTYPE_python3</text></name>
                <questiontext format="html"><text>x</text></questiontext>
                <coderunnertype>python3</coderunnertype><prototypetype>1</prototypetype></question>
            </quiz>
            """;

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Questions);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task ImportAsync_StoresTheCodeQuestion_AndReportsAnInvalidOne()
    {
        using var context = TestDbContextFactory.Create();
        var section = new Section { Name = "Програмиране", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();
        var noOutput = CodeRunner(OneTest.Replace("<expected><text>1</text></expected>", "<expected><text></text></expected>"))
            .Replace("<quiz>", "").Replace("</quiz>", "");
        var xml = PythonQuestion.Replace("</quiz>", noOutput + "</quiz>");

        var result = await new QuestionBankService(context).ImportAsync(section.Id, xml, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.Added);
        Assert.Contains(result.Value.Errors, e => e.Contains("Всеки тест трябва да има очакван изход."));
        var saved = await context.Questions.Include(q => q.TestCases).SingleAsync();
        Assert.Equal(QuestionType.Code, saved.Type);
        Assert.Equal(2, saved.TestCases.Count);
    }

    // ---------- Export ----------

    private static Question CodeQuestion(string language = CodeLanguages.CSharp) => new()
    {
        Text = "Прочетете две числа.\nОтпечатайте сбора им.",
        Type = QuestionType.Code,
        Points = 4,
        CodeLanguage = language,
        AnswerPreload = "using System;\n\nclass P\n{\n}",
        AllOrNothing = false,
        TestCases =
        {
            new CodeTestCase { Stdin = "2 3", Expected = "5\nГотово", IsExample = true, Order = 1 },
            new CodeTestCase { Stdin = "  -10 4\n", Expected = "-6", IsHidden = true, Weight = 2.5m, Order = 2 },
        },
    };

    [Fact]
    public void Write_CodeQuestion_AsCodeRunner()
    {
        var question = XDocument.Parse(MoodleXmlWriter.Write(new[] { CodeQuestion() })).Root!.Element("question")!;

        Assert.Equal("coderunner", (string?)question.Attribute("type"));
        Assert.Equal("csharp_program", question.Element("coderunnertype")!.Value);
        Assert.Equal("0", question.Element("prototypetype")!.Value);
        Assert.Equal("0", question.Element("allornothing")!.Value);
        Assert.Equal("4", question.Element("defaultgrade")!.Value);
        Assert.Equal("using System;\n\nclass P\n{\n}", question.Element("answerpreload")!.Value);
        Assert.Equal("", question.Element("template")!.Value);
        var tests = question.Element("testcases")!.Elements("testcase").ToList();
        Assert.Equal(2, tests.Count);
        Assert.Equal(("1", "1", "SHOW"), ((string)tests[0].Attribute("useasexample")!, (string)tests[0].Attribute("mark")!, tests[0].Element("display")!.Value));
        Assert.Equal(("0", "2.5", "HIDE"), ((string)tests[1].Attribute("useasexample")!, (string)tests[1].Attribute("mark")!, tests[1].Element("display")!.Value));
        Assert.Equal("  -10 4\n", tests[1].Element("stdin")!.Value);
    }

    [Theory]
    [InlineData(CodeLanguages.Python, "python3")]
    [InlineData(CodeLanguages.CSharp, "csharp_program")]
    public void CodeRunnerType_ByLanguage(string language, string type)
    {
        Assert.Equal(type, MoodleXmlWriter.CodeRunnerType(language));
    }

    [Theory]
    [InlineData(CodeLanguages.Python)]
    [InlineData(CodeLanguages.CSharp)]
    public void Export_ThenImport_GivesTheSameQuestion(string language)
    {
        var original = CodeQuestion(language);

        var input = Assert.Single(MoodleXmlParser.Parse(MoodleXmlWriter.Write(new[] { original })).Questions);

        Assert.Equal(original.Text, input.Text);
        Assert.Equal(original.Points, input.Points);
        Assert.Equal((language, original.AnswerPreload, false), (input.Code!.Language, input.Code.Preload, input.Code.AllOrNothing));
        Assert.Equal(
            original.TestCases.Select(t => new CodeTestInput(t.Stdin, t.Expected, t.IsExample, t.IsHidden, t.Weight)),
            input.Code.Tests);
    }

    [Fact]
    public async Task ExportSection_IncludesTheTests()
    {
        using var context = TestDbContextFactory.Create();
        var section = new Section { Name = "Програмиране", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();
        await new QuestionBankService(context).CreateAsync(section.Id,
            QuestionInput.CodeQuestion("Сбор", CodeLanguages.Python, new[] { new CodeTestInput("2 3", "5") }), Owner, false);
        context.ChangeTracker.Clear();

        var export = (await new QuestionExportService(context).ExportSectionAsync(section.Id, Owner, false)).Value!;

        Assert.Contains("<coderunnertype>python3</coderunnertype>", export.Content);
        Assert.Contains("<stdin>", export.Content);
        Assert.Single(MoodleXmlParser.Parse(export.Content).Questions);
    }
}
