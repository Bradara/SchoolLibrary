using SchoolLibrary.Data.Enums;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class MoodleXmlParserTests
{
    private static string Quiz(string questions) => $"<?xml version=\"1.0\"?><quiz>{questions}</quiz>";

    [Fact]
    public void Parse_ReadsAMultichoiceQuestionWithItsCorrectAnswer()
    {
        var xml = Quiz(
            "<question type=\"multichoice\"><single>true</single>" +
            "<questiontext><text><![CDATA[<p>Колко е 2 + 2?</p>]]></text></questiontext>" +
            "<answer fraction=\"0\"><text>3</text></answer>" +
            "<answer fraction=\"100\"><text>4</text></answer>" +
            "<answer fraction=\"0\"><text>5</text></answer></question>");

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Errors);
        var q = Assert.Single(result.Questions);
        Assert.Equal("Колко е 2 + 2?", q.Text); // HTML stripped
        Assert.Equal(new[] { "3", "4", "5" }, q.Options);
        Assert.Equal(1, q.CorrectIndex);
    }

    [Fact]
    public void Parse_ReadsATrueFalseQuestion()
    {
        var xml = Quiz(
            "<question type=\"truefalse\">" +
            "<questiontext><text>Земята е кръгла.</text></questiontext>" +
            "<answer fraction=\"100\"><text>Вярно</text></answer>" +
            "<answer fraction=\"0\"><text>Грешно</text></answer></question>");

        var result = MoodleXmlParser.Parse(xml);

        // Unknown answer texts: the first answer means "true"; the options become the fixed pair
        var q = Assert.Single(result.Questions);
        Assert.Equal(QuestionType.TrueFalse, q.Type);
        Assert.Equal(0, q.CorrectIndex);
        Assert.Equal(new[] { "Вярно", "Невярно" }, q.Options);
    }

    [Theory]
    [InlineData("<answer fraction=\"100\"><text>true</text></answer><answer fraction=\"0\"><text>false</text></answer>", 0)]
    [InlineData("<answer fraction=\"0\"><text>true</text></answer><answer fraction=\"100\"><text>false</text></answer>", 1)]
    [InlineData("<answer fraction=\"100\"><text>false</text></answer><answer fraction=\"0\"><text>true</text></answer>", 1)]
    [InlineData("<answer fraction=\"0\"><text>Вярно</text></answer><answer fraction=\"100\"><text>Невярно</text></answer>", 1)]
    public void Parse_TrueFalse_ReadsTheCorrectValue(string answers, int expectedIndex)
    {
        var xml = Quiz(
            "<question type=\"truefalse\"><name><text>ТФ</text></name>" +
            "<questiontext format=\"html\"><text><![CDATA[<p>Слънцето е звезда.</p>]]></text></questiontext>" +
            "<defaultgrade>1.0000000</defaultgrade>" + answers + "</question>");

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Errors);
        var q = Assert.Single(result.Questions);
        Assert.Equal(QuestionType.TrueFalse, q.Type);
        Assert.Equal("Слънцето е звезда.", q.Text);
        Assert.Equal(new[] { "Вярно", "Невярно" }, q.Options);
        Assert.Equal(expectedIndex, q.CorrectIndex);
    }

    [Fact]
    public void Parse_TrueFalseWithoutACorrectAnswer_IsReported()
    {
        var xml = Quiz(
            "<question type=\"truefalse\"><name><text>ТФ</text></name>" +
            "<questiontext><text>Въпрос</text></questiontext>" +
            "<answer fraction=\"0\"><text>true</text></answer><answer fraction=\"0\"><text>false</text></answer></question>");

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Questions);
        Assert.Contains("„ТФ“: няма посочен верен отговор.", result.Errors);
    }

    [Fact]
    public void Parse_Multichoice_IsASingleChoiceQuestion()
    {
        var xml = Quiz(
            "<question type=\"multichoice\"><single>true</single>" +
            "<questiontext><text>2 + 2?</text></questiontext>" +
            "<answer fraction=\"100\"><text>4</text></answer><answer fraction=\"0\"><text>5</text></answer></question>");

        Assert.Equal(QuestionType.SingleChoice, Assert.Single(MoodleXmlParser.Parse(xml).Questions).Type);
    }

    [Fact]
    public void Parse_SkipsTheCategoryMarkerWithoutAnError()
    {
        var xml = Quiz(
            "<question type=\"category\"><category><text>$course$/По подразбиране</text></category></question>" +
            "<question type=\"multichoice\"><single>true</single>" +
            "<questiontext><text>В?</text></questiontext>" +
            "<answer fraction=\"100\"><text>а</text></answer>" +
            "<answer fraction=\"0\"><text>б</text></answer></question>");

        var result = MoodleXmlParser.Parse(xml);

        Assert.Single(result.Questions);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_ReportsUnsupportedQuestionTypes()
    {
        var xml = Quiz("<question type=\"essay\"><name><text>Есе</text></name>" +
                       "<questiontext><text>Напишете…</text></questiontext></question>");

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Questions);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ReportsAMultiAnswerMultichoiceAsUnsupported()
    {
        var xml = Quiz(
            "<question type=\"multichoice\"><single>false</single>" +
            "<questiontext><text>Кои са прости?</text></questiontext>" +
            "<answer fraction=\"50\"><text>2</text></answer>" +
            "<answer fraction=\"50\"><text>3</text></answer></question>");

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Questions);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ReportsAQuestionWithoutACorrectAnswer()
    {
        var xml = Quiz(
            "<question type=\"multichoice\"><single>true</single>" +
            "<questiontext><text>В?</text></questiontext>" +
            "<answer fraction=\"0\"><text>а</text></answer>" +
            "<answer fraction=\"0\"><text>б</text></answer></question>");

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Questions);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ReportsTooFewOptions()
    {
        var xml = Quiz(
            "<question type=\"multichoice\"><single>true</single>" +
            "<questiontext><text>В?</text></questiontext>" +
            "<answer fraction=\"100\"><text>само един</text></answer></question>");

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Questions);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ReportsAMissingQuestionText()
    {
        var xml = Quiz(
            "<question type=\"multichoice\"><single>true</single>" +
            "<questiontext><text></text></questiontext>" +
            "<answer fraction=\"100\"><text>а</text></answer>" +
            "<answer fraction=\"0\"><text>б</text></answer></question>");

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Questions);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_InvalidXml_ReportsOneError()
    {
        var result = MoodleXmlParser.Parse("<quiz><question></quiz>");

        Assert.Empty(result.Questions);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_EmptyText_ReturnsNothing()
    {
        var result = MoodleXmlParser.Parse("   ");

        Assert.Empty(result.Questions);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_KeepsGoodQuestionsAndReportsTheBadOne()
    {
        var xml = Quiz(
            "<question type=\"multichoice\"><single>true</single>" +
            "<questiontext><text>Добър</text></questiontext>" +
            "<answer fraction=\"100\"><text>а</text></answer>" +
            "<answer fraction=\"0\"><text>б</text></answer></question>" +
            "<question type=\"essay\"><questiontext><text>Есе</text></questiontext></question>");

        var result = MoodleXmlParser.Parse(xml);

        Assert.Single(result.Questions);
        Assert.Single(result.Errors);
    }
}
