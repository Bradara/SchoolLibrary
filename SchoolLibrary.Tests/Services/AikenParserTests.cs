using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class AikenParserTests
{
    [Fact]
    public void Parse_ReadsAQuestionWithOptionsAndTheCorrectAnswer()
    {
        var text = "Колко е 2 + 2?\nA. 3\nB. 4\nC. 5\nANSWER: B";

        var result = AikenParser.Parse(text);

        Assert.Empty(result.Errors);
        var q = Assert.Single(result.Questions);
        Assert.Equal("Колко е 2 + 2?", q.Text);
        Assert.Equal(new[] { "3", "4", "5" }, q.Options);
        Assert.Equal(1, q.CorrectIndex); // B
    }

    [Fact]
    public void Parse_ReadsSeveralQuestionsSeparatedByBlankLines()
    {
        var text = "Въпрос 1\nA. а\nB. б\nANSWER: A\n\nВъпрос 2\nA. в\nB. г\nANSWER: B";

        var result = AikenParser.Parse(text);

        Assert.Equal(2, result.Questions.Count);
        Assert.Equal(0, result.Questions[0].CorrectIndex);
        Assert.Equal(1, result.Questions[1].CorrectIndex);
    }

    [Fact]
    public void Parse_AcceptsBothDotAndParenthesisAndIsCaseInsensitiveForAnswer()
    {
        var text = "В?\nA) а\nB) б\nanswer: b";

        var result = AikenParser.Parse(text);

        var q = Assert.Single(result.Questions);
        Assert.Equal(1, q.CorrectIndex);
    }

    [Fact]
    public void Parse_ReportsAMissingAnswerLine()
    {
        var text = "В?\nA. а\nB. б";

        var result = AikenParser.Parse(text);

        Assert.Empty(result.Questions);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ReportsTooFewOptions()
    {
        var text = "В?\nA. само един\nANSWER: A";

        var result = AikenParser.Parse(text);

        Assert.Empty(result.Questions);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ReportsAnswerPointingToAMissingOption()
    {
        var text = "В?\nA. а\nB. б\nANSWER: C";

        var result = AikenParser.Parse(text);

        Assert.Empty(result.Questions);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_ReportsAMissingQuestionText()
    {
        var text = "A. а\nB. б\nANSWER: A";

        var result = AikenParser.Parse(text);

        Assert.Empty(result.Questions);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_KeepsGoodQuestionsAndReportsTheBadOne()
    {
        var text = "Добър\nA. а\nB. б\nANSWER: A\n\nЛош\nA. а\nB. б"; // second has no ANSWER

        var result = AikenParser.Parse(text);

        Assert.Single(result.Questions);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_EmptyText_ReturnsNothing()
    {
        var result = AikenParser.Parse("   ");

        Assert.Empty(result.Questions);
        Assert.Empty(result.Errors);
    }
}
