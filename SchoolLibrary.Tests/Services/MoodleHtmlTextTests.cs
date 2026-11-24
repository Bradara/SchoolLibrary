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
    [InlineData("<p>Първи</p><p>Втори</p>", "Първи Втори")]
    [InlineData("ред<br>ред<br/>ред", "ред ред ред")]
    [InlineData("<ul><li>а</li><li>б</li></ul>", "а б")]
    [InlineData("<table><tr><td>1</td><td>2</td></tr></table>", "1 2")]
    [InlineData("<div>а</div><div>б</div>", "а б")]
    public void BlockTags_SeparateWords(string html, string expected)
    {
        Assert.Equal(expected, Text(html));
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
