using SchoolLibrary.Data.Enums;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>How the HTML of a Moodle question becomes plain text: inline tags keep words together.</summary>
public class MoodleHtmlTextTests
{
    private static string Text(string html)
    {
        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"multichoice\"><single>true</single>" +
                  $"<questiontext><text><![CDATA[{html}]]></text></questiontext>" +
                  "<answer fraction=\"100\"><text>Да</text></answer><answer fraction=\"0\"><text>Не</text></answer></question></quiz>";
        return Assert.Single(MoodleXmlParser.Parse(xml).Questions).Text;
    }

    [Theory]
    // Word's markup from a real export: spans and bold inside words and formulas
    [InlineData("<p><span lang=\"BG\">време </span>O<span lang=\"RU\">(</span>f<span lang=\"RU\">(</span>n<span lang=\"RU\">))</span></p>", "време O(f(n))")]
    [InlineData("в <b>най</b></span><b><span lang=\"RU\">-</span></b><b><span lang=\"BG\">лошия</span></b> случай", "в най-лошия случай")]
    [InlineData("H<sub>2</sub>O и x<sup>2</sup>", "H2O и x2")]
    [InlineData("<a href=\"https://example.com\">връзка</a>.", "връзка.")]
    public void InlineTags_LeaveNoSpace(string html, string expected)
    {
        Assert.Equal(expected, Text(html));
    }

    [Theory]
    [InlineData("<p>Първи</p><p>Втори</p>", "Първи\nВтори")]
    [InlineData("ред<br>ред<br/>ред", "ред\nред\nред")]
    [InlineData("<ul><li>а</li><li>б</li></ul>", "а\nб")]
    [InlineData("<table><tr><td>1</td><td>2</td></tr><tr><td>3</td></tr></table>", "1 2\n3")]
    [InlineData("<div>а</div><div>б</div>", "а\nб")]
    [InlineData("<p>1. [[1]]<br>2. [[2]]</p><p></p><p></p><p>Край</p>", "1. [[1]]\n2. [[2]]\n\nКрай")]
    public void BlockTags_BreakLines(string html, string expected)
    {
        Assert.Equal(expected, Text(html));
    }

    [Fact]
    public void LineBreaksOfTheHtmlSource_AreSpaces()
    {
        Assert.Equal("Един дълъг абзац\nВтори", Text("\n<p>Един\r\n  дълъг\nабзац</p>\n\n<p>Втори</p>\n"));
    }

    [Theory]
    [InlineData("plain_text")]
    [InlineData("moodle_auto_format")]
    [InlineData("markdown")]
    public void TextFormats_KeepTheWrittenLineBreaks(string format)
    {
        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"truefalse\">" +
                  $"<questiontext format=\"{format}\"><text>Първи ред\nвтори ред</text></questiontext>" +
                  "<answer fraction=\"100\"><text>true</text></answer><answer fraction=\"0\"><text>false</text></answer></question></quiz>";

        Assert.Equal("Първи ред\nвтори ред", Assert.Single(MoodleXmlParser.Parse(xml).Questions).Text);
    }

    [Theory]
    [InlineData("а\r\nб", QuestionType.SingleChoice, "а\nб")]
    [InlineData("  а   \n\n\n\nб  \n", QuestionType.Matching, "а\n\nб")]
    [InlineData("а\rб", QuestionType.Description, "а\nб")]
    [InlineData("Сума\r\nна две\n числа", QuestionType.Code, "Сума на две числа")]   // the title of a code question
    public void NormalizeText_UnifiesTheLines(string text, QuestionType type, string expected)
    {
        Assert.Equal(expected, QuestionContent.NormalizeText(text, type));
    }

    [Fact]
    public void Validation_CountsALineBreakAsOneCharacter()
    {
        // The browser counts a line break as one character for maxlength but sends it as \r\n
        var text = string.Join("\r\n", Enumerable.Repeat(new string('а', 9), 100)).TrimEnd();   // 999 when unified
        var input = new QuestionInput(text, new[] { "Да", "Не" }, 0);

        Assert.Null(QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Spaces_AndEntities_AreCollapsed()
    {
        Assert.Equal("а & б", Text("<p>  а&nbsp;&amp;   <span> б</span> </p>"));
    }

    [Fact]
    public void CodeStatement_KeepsLines_AndJoinsInlineTags()
    {
        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"coderunner\"><name><text>Сума</text></name>" +
                  "<questiontext><text><![CDATA[<p>Прочетете <b>две</b> числа: <i>a</i>, <i>b</i>.</p><p>Отпечатайте a<sup>2</sup>.</p>]]></text></questiontext>" +
                  "<coderunnertype>python3</coderunnertype>" +
                  "<testcases><testcase testtype=\"0\"><stdin><text>1 2</text></stdin><expected><text>1</text></expected></testcase></testcases>" +
                  "</question></quiz>";

        var q = Assert.Single(MoodleXmlParser.Parse(xml).Questions);

        Assert.Equal("Прочетете две числа: a, b.\nОтпечатайте a2.", q.Code!.Statement);
    }
}
