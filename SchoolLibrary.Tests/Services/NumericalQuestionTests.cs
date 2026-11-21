using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Services;

/// <summary>Numerical questions end to end: reading numbers, grading, validation, import, storage and the form.</summary>
public class NumericalQuestionTests
{
    private const string Owner = "teacher-1";

    // ---------- NumericAnswer ----------

    [Theory]
    [InlineData("3,14", 3.14)]
    [InlineData("3.14", 3.14)]
    [InlineData("  42 ", 42)]
    [InlineData("-2,5", -2.5)]
    [InlineData("−2,5", -2.5)]        // Unicode minus sign
    [InlineData("1 000", 1000)]       // space between digit groups
    [InlineData("1 000,5", 1000.5)] // non-breaking space
    [InlineData("1e3", 1000)]
    [InlineData("2,5E-1", 0.25)]
    [InlineData(",5", 0.5)]
    [InlineData("0", 0)]
    public void Parse_ReadsNumbers(string text, double expected)
    {
        Assert.Equal((decimal)expected, NumericAnswer.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("три")]
    [InlineData("1,000.5")]           // both separators: ambiguous
    [InlineData("1,2,3")]
    [InlineData("3,14 м")]            // units are not supported
    [InlineData("1e999")]             // too large for decimal
    [InlineData("12345678901234567890123456789012345678901234567890123")] // too long
    public void Parse_RejectsWhatIsNotANumber(string? text)
    {
        Assert.Null(NumericAnswer.Parse(text));
    }

    [Theory]
    [InlineData(3.14, "3.14")]
    [InlineData(2.50, "2.5")]
    [InlineData(-7, "-7")]
    [InlineData(0.000001, "0.000001")]
    public void Format_UsesAPointAndNoTrailingZeros(double number, string expected)
    {
        Assert.Equal(expected, NumericAnswer.Format((decimal)number));
    }

    [Fact]
    public void Display_UsesADecimalComma()
    {
        Assert.Equal("3,14", NumericAnswer.Display("3.14"));
        Assert.Equal("всяко число", NumericAnswer.Display(NumericAnswer.AnyNumber));
        Assert.Equal("0,01", NumericAnswer.Display(0.0100m));
    }

    // ---------- NumericalGrader ----------

    private static Question Question(params (string Value, decimal Tolerance, decimal Fraction)[] accepted) => new()
    {
        Id = 4,
        Type = QuestionType.Numerical,
        Points = 2,
        Options = (accepted.Length > 0 ? accepted : new[] { (Value: "3.14", Tolerance: 0.01m, Fraction: 100m) })
            .Select((a, i) => new AnswerOption { Id = i + 1, Text = a.Value, Tolerance = a.Tolerance, Fraction = a.Fraction, Order = i + 1 })
            .ToList()
    };

    private static QuestionGrade Grade(Question question, string? typed)
        => new NumericalGrader().Grade(question, SubmittedAnswer.Typed(typed));

    [Fact]
    public void Grader_IsRegistered()
    {
        Assert.IsType<NumericalGrader>(QuestionGraders.For(QuestionType.Numerical));
    }

    [Theory]
    [InlineData("3,14")]
    [InlineData("3.14")]
    [InlineData("3,13")]   // the lower edge of the tolerance
    [InlineData("3,15")]   // the upper edge
    [InlineData("3,145")]
    public void Grader_WithinTheTolerance_EarnsAllPoints(string typed)
    {
        var grade = Grade(Question(), typed);

        Assert.Equal(2m, grade.AwardedPoints);
        Assert.True(grade.IsCorrect);
        Assert.Equal(typed, grade.Answer.Text);
    }

    [Theory]
    [InlineData("3,12")]
    [InlineData("3,151")]
    [InlineData("-3,14")]
    public void Grader_OutsideTheTolerance_EarnsNothing(string typed)
    {
        var grade = Grade(Question(), typed);

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
        Assert.Equal(typed, grade.Answer.Text);
    }

    [Fact]
    public void Grader_ZeroTolerance_NeedsTheExactNumber()
    {
        var question = Question(("12", 0m, 100m));

        Assert.True(Grade(question, "12").IsCorrect);
        Assert.True(Grade(question, "12,0").IsCorrect);
        Assert.False(Grade(question, "12,0001").IsCorrect);
    }

    [Fact]
    public void Grader_TextThatIsNotANumber_EarnsNothing_ButIsKept()
    {
        var grade = Grade(Question(), "пи");

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.Equal("пи", grade.Answer.Text);
    }

    [Fact]
    public void Grader_Blank_IsUnanswered()
    {
        Assert.Same(SubmittedAnswer.Empty, Grade(Question(), " ").Answer);
    }

    [Fact]
    public void Grader_TheBestMatchCounts_AndAnyNumberCatchesTheRest()
    {
        var question = Question(("*", 0m, 0m), ("3", 0.5m, 50m), ("3.14", 0.01m, 100m));

        Assert.Equal(2m, Grade(question, "3,14").AwardedPoints);
        Assert.Equal(1m, Grade(question, "3,4").AwardedPoints);
        Assert.Equal(0m, Grade(question, "100").AwardedPoints);
    }

    [Fact]
    public void Grader_HugeNumbers_DoNotOverflow()
    {
        var question = Question(("70000000000000000000000000000", 0m, 100m));

        Assert.False(Grade(question, "-70000000000000000000000000000").IsCorrect);
    }

    // ---------- Validation ----------

    [Fact]
    public void Input_Numerical_GivesEveryValueFullCredit()
    {
        var input = QuestionInput.Numerical("π?", new[] { "3,14", "*" }, new decimal?[] { 0.01m, 0m }, 3);

        Assert.Equal(QuestionType.Numerical, input.Type);
        Assert.Equal(new[] { 100m, 100m }, input.Fractions);
        Assert.Equal(3, input.Points);
        Assert.Null(QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_ValueThatIsNotANumber_IsRejected()
    {
        var input = QuestionInput.Numerical("π?", new[] { "пи" }, new decimal?[] { 0m });

        Assert.Equal("Приемливите отговори трябва да са числа (например 3,14) или „*“ за всяко число.", QuizService.ValidateQuestion(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-0.5)]
    public void Validate_BadTolerance_IsRejected(double? tolerance)
    {
        var input = QuestionInput.Numerical("π?", new[] { "3,14" }, new[] { (decimal?)tolerance });

        Assert.Equal("Толерансът трябва да е число, по-голямо или равно на 0 (например 0,01).", QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_NoValue_IsRejected()
    {
        var input = QuestionInput.Numerical("π?", Array.Empty<string>(), Array.Empty<decimal?>());

        Assert.Equal("Добавете поне един приемлив отговор.", QuizService.ValidateQuestion(input));
    }

    // ---------- Import ----------

    [Fact]
    public void Parse_Numerical_KeepsTheValuesTolerancesAndShares()
    {
        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"numerical\">" +
                  "<questiontext><text>Колко е π с 2 знака?</text></questiontext>" +
                  "<answer fraction=\"100\"><text>3.14</text><tolerance>0.005</tolerance></answer>" +
                  "<answer fraction=\"50\"><text>3</text><tolerance>0.2</tolerance></answer>" +
                  "<answer fraction=\"0\"><text>*</text></answer>" +
                  "<units><unit><multiplier>1</multiplier><unit_name>m</unit_name></unit></units>" +
                  "</question></quiz>";

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Errors);
        var q = Assert.Single(result.Questions);
        Assert.Equal(QuestionType.Numerical, q.Type);
        Assert.Equal(new[] { "3.14", "3", "*" }, q.Options);
        Assert.Equal(new decimal?[] { 0.005m, 0.2m, 0m }, q.Tolerances);
        Assert.Equal(new[] { 100m, 50m, 0m }, q.Fractions);
        Assert.Null(QuizService.ValidateQuestion(q));
    }

    [Fact]
    public void Parse_Numerical_WithAValueThatIsNotANumber_IsReported()
    {
        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"numerical\"><name><text>Ч</text></name>" +
                  "<questiontext><text>π?</text></questiontext>" +
                  "<answer fraction=\"100\"><text>пи</text></answer></question></quiz>";

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Questions);
        Assert.Contains("„Ч“: приемливите отговори трябва да са числа.", result.Errors);
    }

    // ---------- Storage and results ----------

    [Fact]
    public async Task AddQuestionAsync_StoresTheValuesWithAPointAndTheTolerances()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Упражнение", null, QuizMode.Practice, null, null), Owner)).Value!;

        await service.AddQuestionAsync(quiz.Id,
            QuestionInput.Numerical("π?", new[] { "3,14", " 1 000 ", "*" }, new decimal?[] { 0.01m, 0m, 0m }, 2), Owner, false);

        var saved = (await service.GetByIdAsync(quiz.Id))!.Questions.Single();
        var options = saved.Options.OrderBy(o => o.Order).ToList();
        Assert.Equal(QuestionType.Numerical, saved.Type);
        Assert.Equal(new[] { "3.14", "1000", "*" }, options.Select(o => o.Text));
        Assert.Equal(new decimal?[] { 0.01m, 0m, 0m }, options.Select(o => o.Tolerance));
    }

    [Fact]
    public async Task OtherQuestionTypes_HaveNoTolerance()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Упражнение", null, QuizMode.Practice, null, null), Owner)).Value!;

        await service.AddQuestionAsync(quiz.Id, new QuestionInput("2 + 2?", new[] { "4", "5" }, 0), Owner, false);

        Assert.All((await service.GetByIdAsync(quiz.Id))!.Questions.Single().Options, o => Assert.Null(o.Tolerance));
    }

    [Fact]
    public async Task SubmitExamAsync_StoresTheTypedNumberAndRebuildsTheResult()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id, QuestionInput.Numerical("π?", new[] { "3,14" }, new decimal?[] { 0.01m }, 2), Owner, false);
        await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        var exam = (await service.GetByIdAsync(quiz.Id))!;
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var question = (await service.GetExamAttemptAsync(attempt.Token))!.Questions.Single().Question;

        var result = await service.SubmitExamAsync(attempt.Token,
            SubmittedAnswer.FromForm(null, null, new Dictionary<int, string?> { [question.Id] = "3,141" }));

        Assert.Equal(2m, result.Value!.Score);
        Assert.Equal("3,141", (await context.AttemptAnswers.SingleAsync(a => a.AttemptId == attempt.Id)).ResponseText);

        var rebuilt = (await service.GetExamResultAsync(attempt.Token)).Value!;
        Assert.Equal(2m, rebuilt.Score);
        Assert.Equal("3,141", rebuilt.Questions.Single().TypedText);
    }

    // ---------- The question form ----------

    [Fact]
    public void Form_Numerical_MapsValuesAndTolerances()
    {
        var form = new QuestionFormViewModel
        {
            Type = QuestionType.Numerical,
            Text = "π?",
            Options = new List<string> { "3,14", "", "3" },
            Tolerances = new List<string> { "0,01", "5", "" },   // the second row is empty and is dropped
            Points = 2
        };

        var input = form.ToInput();

        Assert.Equal(QuestionType.Numerical, input.Type);
        Assert.Equal(new[] { "3,14", "3" }, input.Options);
        Assert.Equal(new decimal?[] { 0.01m, 0m }, input.Tolerances);   // blank tolerance = exact
        Assert.Equal(new[] { 100m, 100m }, input.Fractions);
        Assert.Null(QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Form_ToleranceThatIsNotANumber_IsRejectedByTheService()
    {
        var form = new QuestionFormViewModel
        {
            Type = QuestionType.Numerical,
            Text = "π?",
            Options = new List<string> { "3,14" },
            Tolerances = new List<string> { "малко" }
        };

        Assert.Equal("Толерансът трябва да е число, по-голямо или равно на 0 (например 0,01).",
            QuizService.ValidateQuestion(form.ToInput()));
    }

    [Fact]
    public void Form_FromNumericalQuestion_ShowsCommas()
    {
        var form = QuestionFormViewModel.FromQuestion(Question(("3.14", 0.01m, 100m), ("*", 0m, 100m)));

        Assert.Equal(QuestionType.Numerical, form.Type);
        Assert.Equal(new[] { "3,14", "*" }, form.Options);
        Assert.Equal(new[] { "0,01", "0" }, form.Tolerances);
        Assert.False(form.HasCustomShares);
    }

    [Fact]
    public void Form_FromImportedPartialValues_WarnsThatSavingReplacesThem()
    {
        Assert.True(QuestionFormViewModel.FromQuestion(Question(("3.14", 0m, 100m), ("3", 0m, 50m))).HasCustomShares);
    }
}
