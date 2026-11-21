using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Tests;

public class CodeTestCaseTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void NewTestCase_HasWeightOneAndEmptyTexts()
    {
        var test = new CodeTestCase();

        Assert.Equal(1m, test.Weight);
        Assert.Equal("", test.Stdin);
        Assert.Equal("", test.Expected);
        Assert.False(test.IsExample);
        Assert.False(test.IsHidden);
    }

    [Fact]
    public void EmptyInputAndOutput_AreValid()
    {
        Assert.Empty(Validate(new CodeTestCase()));
    }

    [Fact]
    public void TooLongTexts_AreInvalid()
    {
        var tooLong = new string('x', CodeTestCase.MaxTextLength + 1);

        var results = Validate(new CodeTestCase { Stdin = tooLong, Expected = tooLong });

        Assert.Equal(2, results.Count);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(2.5, true)]
    [InlineData(1000, true)]
    [InlineData(1000.5, false)]
    public void Weight_IsBetweenZeroAndAThousand(double weight, bool valid)
    {
        Assert.Equal(valid, Validate(new CodeTestCase { Weight = (decimal)weight }).Count == 0);
    }

    [Fact]
    public void TooLongPreload_IsInvalid()
    {
        var question = new Question { Text = "Сума", Type = QuestionType.Code, AnswerPreload = new string('x', 10001) };

        Assert.Contains(Validate(question), r => r.MemberNames.Contains(nameof(Question.AnswerPreload)));
    }

    [Fact]
    public async Task CodeQuestion_IsSavedWithItsTests_AndDeletingItRemovesThem()
    {
        await using var db = TestDbContextFactory.Create();
        var question = new Question
        {
            Text = "Сума на две числа",
            Type = QuestionType.Code,
            CodeLanguage = "csharp",
            AnswerPreload = "class P { static void Main() { } }",
            TestCases =
            {
                new CodeTestCase { Stdin = "2 3", Expected = "5", IsExample = true, Order = 0 },
                new CodeTestCase { Stdin = "-10 4", Expected = "-6", IsHidden = true, Weight = 2, Order = 1 },
            },
        };
        db.Questions.Add(question);
        await db.SaveChangesAsync();

        var saved = await db.Questions.Include(q => q.TestCases).SingleAsync();
        Assert.Equal(QuestionType.Code, saved.Type);
        Assert.Equal("csharp", saved.CodeLanguage);
        Assert.Equal(new[] { "5", "-6" }, saved.TestCases.OrderBy(t => t.Order).Select(t => t.Expected));

        db.Questions.Remove(saved);
        await db.SaveChangesAsync();

        Assert.Empty(db.CodeTestCases);
    }
}
