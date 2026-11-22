using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Services;

/// <summary>Code questions: language, starting code, scoring and test cases, created and edited like the other types.</summary>
public class CodeQuestionTests
{
    private const string Owner = "teacher-1";

    private static readonly CodeTestInput[] TwoTests =
    {
        new("2 3", "5", IsExample: true),
        new("-10 4", "-6", IsHidden: true, Weight: 2m),
    };

    private static QuestionInput Input(string language = "csharp", IReadOnlyList<CodeTestInput>? tests = null,
        string? preload = null, bool allOrNothing = true, int points = 3)
        => QuestionInput.CodeQuestion("Отпечатайте сбора на две числа.", language, tests ?? TwoTests, preload, allOrNothing, points);

    private static async Task<(QuestionBankService Service, Section Section)> BankAsync(Data.ApplicationDbContext context)
    {
        var section = new Section { Name = "Програмиране", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        context.Sections.Add(section);
        await context.SaveChangesAsync();
        return (new QuestionBankService(context), section);
    }

    // ---------- Validation ----------

    [Fact]
    public void Validate_ACodeQuestion_IsValid()
    {
        var input = Input(preload: "class P { static void Main() { } }");

        Assert.Equal(QuestionType.Code, input.Type);
        Assert.Empty(input.Options);
        Assert.Null(QuizService.ValidateQuestion(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("rust")]
    public void Validate_UnknownLanguage_IsRejected(string language)
    {
        Assert.Equal("Изберете език за програмиране.", QuizService.ValidateQuestion(Input(language)));
    }

    [Fact]
    public void Validate_NoTests_IsRejected()
    {
        Assert.Equal("Добавете поне един тест.", QuizService.ValidateQuestion(Input(tests: Array.Empty<CodeTestInput>())));
    }

    [Fact]
    public void Validate_TooManyTests_IsRejected()
    {
        var tests = Enumerable.Range(0, QuizService.MaxCodeTests + 1).Select(i => new CodeTestInput($"{i}", $"{i}")).ToList();

        Assert.Equal($"Тестовете не може да са повече от {QuizService.MaxCodeTests}.", QuizService.ValidateQuestion(Input(tests: tests)));
    }

    [Fact]
    public void Validate_TestWithoutExpectedOutput_IsRejected()
    {
        Assert.Equal("Всеки тест трябва да има очакван изход.",
            QuizService.ValidateQuestion(Input(tests: new[] { new CodeTestInput("1", "  ") })));
    }

    [Fact]
    public void Validate_EmptyInput_IsAllowed()
    {
        Assert.Null(QuizService.ValidateQuestion(Input(tests: new[] { new CodeTestInput("", "Здравей!") })));
    }

    [Fact]
    public void Validate_TooLongTestText_IsRejected()
    {
        var tooLong = new string('1', CodeTestCase.MaxTextLength + 1);

        Assert.NotNull(QuizService.ValidateQuestion(Input(tests: new[] { new CodeTestInput(tooLong, "1") })));
        Assert.NotNull(QuizService.ValidateQuestion(Input(tests: new[] { new CodeTestInput("1", tooLong) })));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000.5)]
    public void Validate_WeightOutOfRange_IsRejected(double weight)
    {
        Assert.Equal("Тежестта на тест трябва да е между 0 и 1000.",
            QuizService.ValidateQuestion(Input(tests: new[] { new CodeTestInput("1", "1", Weight: (decimal)weight) })));
    }

    [Fact]
    public void Validate_TooLongPreload_IsRejected()
    {
        Assert.NotNull(QuizService.ValidateQuestion(Input(preload: new string('x', CodeGrader.MaxCodeLength + 1))));
    }

    [Fact]
    public void Validate_PointsOutOfRange_IsRejected()
    {
        Assert.NotNull(QuizService.ValidateQuestion(Input(points: 0)));
    }

    [Fact]
    public void Validate_CodeTypeWithoutCodeSettings_IsRejected()
    {
        Assert.Equal("Неподдържан вид въпрос.",
            QuizService.ValidateQuestion(new QuestionInput("Сума", Array.Empty<string>(), -1, 1, QuestionType.Code)));
    }

    [Fact]
    public void Validate_CodeSettingsOnAnotherType_AreRejected()
    {
        var input = new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0) { Code = Input().Code };

        Assert.Equal("Неподдържан вид въпрос.", QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_CodeQuestionWithOptions_IsRejected()
    {
        Assert.Equal("Неподдържан вид въпрос.", QuizService.ValidateQuestion(Input() with { Options = new[] { "4" } }));
    }

    // ---------- Storage ----------

    [Fact]
    public async Task CreateAsync_StoresTheSettingsAndTestsInOrder()
    {
        using var context = TestDbContextFactory.Create();
        var (service, section) = await BankAsync(context);
        var tests = new[] { new CodeTestInput("2 3\r\n", "5\r\n", IsExample: true), new CodeTestInput("  1 1", "2", Weight: 0.5m) };

        var result = await service.CreateAsync(section.Id, Input(tests: tests, preload: "using System;\n\n", allOrNothing: false), Owner, false);

        Assert.True(result.Succeeded);
        context.ChangeTracker.Clear();
        var saved = (await service.GetQuestionAsync(result.Value!.Id))!;
        Assert.Equal(QuestionType.Code, saved.Type);
        Assert.Equal("csharp", saved.CodeLanguage);
        Assert.Equal("using System;", saved.AnswerPreload);
        Assert.False(saved.AllOrNothing);
        Assert.Equal(3, saved.Points);
        Assert.Empty(saved.Options);
        Assert.Collection(saved.TestCases,
            t =>
            {
                Assert.Equal(("2 3\n", "5\n", true, false, 1m, 1), (t.Stdin, t.Expected, t.IsExample, t.IsHidden, t.Weight, t.Order));
            },
            t =>
            {
                // Leading spaces may matter to the program, so the input is kept as typed
                Assert.Equal(("  1 1", "2", 0.5m, 2), (t.Stdin, t.Expected, t.Weight, t.Order));
            });
    }

    [Fact]
    public async Task CreateAsync_BlankPreload_IsStoredAsNull()
    {
        using var context = TestDbContextFactory.Create();
        var (service, section) = await BankAsync(context);

        var question = (await service.CreateAsync(section.Id, Input(preload: "  \n "), Owner, false)).Value!;

        Assert.Null(question.AnswerPreload);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesTheTests()
    {
        using var context = TestDbContextFactory.Create();
        var (service, section) = await BankAsync(context);
        var question = (await service.CreateAsync(section.Id, Input(), Owner, false)).Value!;
        context.ChangeTracker.Clear();

        var result = await service.UpdateAsync(question.Id, Input("python3", new[] { new CodeTestInput("7", "49") }), Owner, false);

        Assert.True(result.Succeeded);
        context.ChangeTracker.Clear();
        var saved = (await service.GetQuestionAsync(question.Id))!;
        Assert.Equal("python3", saved.CodeLanguage);
        Assert.Equal("49", Assert.Single(saved.TestCases).Expected);
        Assert.Single(context.CodeTestCases); // the old rows are gone
    }

    [Fact]
    public async Task UpdateAsync_ToAnotherType_ClearsTheCodeSettingsAndTests()
    {
        using var context = TestDbContextFactory.Create();
        var (service, section) = await BankAsync(context);
        var question = (await service.CreateAsync(section.Id, Input(preload: "x", allOrNothing: false), Owner, false)).Value!;
        context.ChangeTracker.Clear();

        var result = await service.UpdateAsync(question.Id, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0), Owner, false);

        Assert.True(result.Succeeded);
        context.ChangeTracker.Clear();
        var saved = (await service.GetQuestionAsync(question.Id))!;
        Assert.Equal(QuestionType.SingleChoice, saved.Type);
        Assert.Null(saved.CodeLanguage);
        Assert.Null(saved.AnswerPreload);
        Assert.True(saved.AllOrNothing);
        Assert.Empty(context.CodeTestCases);
        Assert.Equal(2, saved.Options.Count);
    }

    [Fact]
    public async Task UpdateAsync_AnsweredCodeQuestion_IsRefusedForNow()
    {
        using var context = TestDbContextFactory.Create();
        var (service, section) = await BankAsync(context);
        var question = (await service.CreateAsync(section.Id, Input(), Owner, false)).Value!;
        var quiz = new Quiz { Title = "Изпит", OwnerId = Owner };
        context.Quizzes.Add(quiz);
        await context.SaveChangesAsync();
        var attempt = new QuizAttempt { QuizId = quiz.Id, Token = Guid.NewGuid(), StudentName = "Ученик", StartedOn = DateTime.UtcNow, SubmittedOn = DateTime.UtcNow };
        attempt.Questions.Add(new AttemptQuestion { QuestionId = question.Id, Order = 1 });
        attempt.Answers.Add(new AttemptAnswer { QuestionId = question.Id, ResponseText = "print(5)" });
        context.QuizAttempts.Add(attempt);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await service.UpdateAsync(question.Id, Input(tests: new[] { new CodeTestInput("1", "1") }), Owner, false);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal(2, await context.CodeTestCases.CountAsync());
    }

    [Fact]
    public async Task QuizService_AddAndUpdateQuestion_StoresTheTests()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Програмиране", null, QuizMode.Practice, null, null), Owner)).Value!;

        var added = await service.AddQuestionAsync(quiz.Id, Input(), Owner, false);
        Assert.True(added.Succeeded);
        context.ChangeTracker.Clear();

        var updated = await service.UpdateQuestionAsync(added.Value!.Id, Input(tests: new[] { new CodeTestInput("", "Здравей!") }), Owner, false);

        Assert.True(updated.Succeeded);
        context.ChangeTracker.Clear();
        var saved = (await service.GetQuestionAsync(added.Value.Id))!;
        Assert.Equal("Здравей!", Assert.Single(saved.TestCases).Expected);
        Assert.Single(context.CodeTestCases);
    }

    // ---------- Form ----------

    [Fact]
    public void Form_ToInput_DropsEmptyRowsAndReadsTheWeights()
    {
        var form = new QuestionFormViewModel
        {
            Type = QuestionType.Code,
            Text = "Сума",
            Points = 2,
            CodeLanguage = "python3",
            AnswerPreload = "a, b = ",
            AllOrNothing = false,
            Tests =
            {
                new CodeTestFormRow { Stdin = "2 3", Expected = "5", IsExample = true, Weight = "1,5" },
                new CodeTestFormRow { Stdin = " ", Expected = "", Weight = "1" },
                new CodeTestFormRow { Stdin = "", Expected = "0", IsHidden = true, Weight = "" },
                new CodeTestFormRow(),
            },
        };

        var input = form.ToInput();

        Assert.Equal(QuestionType.Code, input.Type);
        Assert.Equal(2, input.Points);
        Assert.Equal(("python3", "a, b = ", false), (input.Code!.Language, input.Code.Preload, input.Code.AllOrNothing));
        Assert.Equal(
            new[] { new CodeTestInput("2 3", "5", true, false, 1.5m), new CodeTestInput("", "0", false, true, 1m) },
            input.Code.Tests);
        Assert.Null(QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Form_ToInput_WeightThatIsNotANumber_IsRejected()
    {
        var form = new QuestionFormViewModel
        {
            Type = QuestionType.Code, Text = "Сума", CodeLanguage = "csharp",
            Tests = { new CodeTestFormRow { Stdin = "1", Expected = "1", Weight = "много" } },
        };

        Assert.Equal("Тежестта на тест трябва да е между 0 и 1000.", QuizService.ValidateQuestion(form.ToInput()));
    }

    [Fact]
    public void Form_ToInput_NoLanguage_IsRejected()
    {
        var form = new QuestionFormViewModel
        {
            Type = QuestionType.Code, Text = "Сума", Tests = { new CodeTestFormRow { Stdin = "1", Expected = "1" } },
        };

        Assert.Equal("Изберете език за програмиране.", QuizService.ValidateQuestion(form.ToInput()));
    }

    [Fact]
    public void Form_FromQuestion_FillsTheCodeFields()
    {
        var question = new Question
        {
            Type = QuestionType.Code, Text = "Сума", Points = 4, CodeLanguage = "csharp", AnswerPreload = "class P {}",
            AllOrNothing = false,
            TestCases =
            {
                new CodeTestCase { Stdin = "b", Expected = "B", Order = 2, IsHidden = true, Weight = 2.5m },
                new CodeTestCase { Stdin = "a", Expected = "A", Order = 1, IsExample = true },
            },
        };

        var form = QuestionFormViewModel.FromQuestion(question);

        Assert.Equal(("csharp", "class P {}", false, 4), (form.CodeLanguage, form.AnswerPreload, form.AllOrNothing, form.Points));
        Assert.Collection(form.Tests,
            t => Assert.Equal(("a", "A", true, false, "1"), (t.Stdin, t.Expected, t.IsExample, t.IsHidden, t.Weight)),
            t => Assert.Equal(("b", "B", false, true, "2,5"), (t.Stdin, t.Expected, t.IsExample, t.IsHidden, t.Weight)));
    }

    // ---------- Statement ----------

    [Fact]
    public async Task Statement_IsStoredWithItsLines_AndClearedForAnotherType()
    {
        using var context = TestDbContextFactory.Create();
        var (service, section) = await BankAsync(context);
        var input = QuestionInput.CodeQuestion("Сбор", "python3", TwoTests,
            statement: "  Прочетете две числа.\r\n\r\nОтпечатайте сбора им.  \r\n");

        var question = (await service.CreateAsync(section.Id, input, Owner, false)).Value!;

        Assert.Equal("Прочетете две числа.\n\nОтпечатайте сбора им.", question.Statement);
        context.ChangeTracker.Clear();

        await service.UpdateAsync(question.Id, new QuestionInput("2 + 2 = ?", new[] { "4", "5" }, 0), Owner, false);
        context.ChangeTracker.Clear();
        Assert.Null((await service.GetQuestionAsync(question.Id))!.Statement);
    }

    [Fact]
    public async Task Statement_Blank_IsStoredAsNull()
    {
        using var context = TestDbContextFactory.Create();
        var (service, section) = await BankAsync(context);

        var question = (await service.CreateAsync(section.Id,
            QuestionInput.CodeQuestion("Сбор", "python3", TwoTests, statement: " \n "), Owner, false)).Value!;

        Assert.Null(question.Statement);
    }

    [Fact]
    public void Validate_TooLongStatement_IsRejected()
    {
        var input = QuestionInput.CodeQuestion("Сбор", "python3", TwoTests, statement: new string('а', QuizService.MaxStatementLength + 1));

        Assert.Equal($"Условието не може да бъде по-дълго от {QuizService.MaxStatementLength} символа.", QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Form_CarriesTheStatement_BothWays()
    {
        var form = QuestionFormViewModel.FromQuestion(new Question
        {
            Type = QuestionType.Code, Text = "Сбор", CodeLanguage = "csharp", Statement = "Ред 1\nРед 2",
            TestCases = { new CodeTestCase { Stdin = "1", Expected = "1" } },
        });

        Assert.Equal("Ред 1\nРед 2", form.Statement);
        Assert.Equal("Ред 1\nРед 2", form.ToInput().Code!.Statement);
    }

    [Fact]
    public void Form_NewForm_IsAllOrNothing()
    {
        Assert.True(new QuestionFormViewModel().AllOrNothing);
        Assert.Equal("1", new CodeTestFormRow().Weight);
    }
}
