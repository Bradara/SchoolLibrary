using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class StoredCodeResultsTests
{
    private static CodeGrade Grade(CodeGradeStatus status = CodeGradeStatus.Graded, string? message = null, params CodeTestResult[] tests)
        => new(status, 1m, false, "print(1)", tests, message);

    [Fact]
    public void RoundTrip_KeepsStatusMessageAndTests()
    {
        var grade = Grade(CodeGradeStatus.Graded, null,
            new CodeTestResult(new CodeTestCase { Id = 11 }, true, "5\nГотово\n", "", false),
            new CodeTestResult(new CodeTestCase { Id = 12 }, false, "", "Traceback", true));

        var json = StoredCodeResults.From(grade).ToJson();
        var read = StoredCodeResults.FromJson(json)!;

        Assert.Contains("\"Graded\"", json); // the status is stored by name, not by number
        Assert.Equal(CodeGradeStatus.Graded, read.Status);
        Assert.Null(read.Message);
        Assert.Equal(
            new[] { new StoredCodeTest(11, true, "5\nГотово\n", "", false), new StoredCodeTest(12, false, "", "Traceback", true) },
            read.Tests);
    }

    [Fact]
    public void CompileError_KeepsTheMessage()
    {
        var read = StoredCodeResults.FromJson(StoredCodeResults.From(Grade(CodeGradeStatus.CompileError, "error CS1001")).ToJson())!;

        Assert.Equal(CodeGradeStatus.CompileError, read.Status);
        Assert.Equal("error CS1001", read.Message);
        Assert.Empty(read.Tests);
    }

    [Fact]
    public void LongTexts_AreCut()
    {
        var longText = new string('x', 5000);
        var grade = Grade(CodeGradeStatus.CompileError, longText,
            new CodeTestResult(new CodeTestCase { Id = 1 }, false, longText, longText, false));

        var stored = StoredCodeResults.From(grade);

        Assert.Equal(StoredCodeResults.MaxMessageLength + 2, stored.Message!.Length);
        Assert.Equal(StoredCodeResults.MaxTextLength + 2, stored.Tests[0].Output.Length);
        Assert.Equal(StoredCodeResults.MaxTextLength + 2, stored.Tests[0].Error.Length);
        Assert.EndsWith("\n…", stored.Tests[0].Output);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Status\":\"NoSuchStatus\"}")]
    public void FromJson_NothingOrUnreadable_IsNull(string? json)
    {
        Assert.Null(StoredCodeResults.FromJson(json));
    }
}
