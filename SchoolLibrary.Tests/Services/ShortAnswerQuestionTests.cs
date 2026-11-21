using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Services;

/// <summary>Short-answer questions end to end: matching, grading, validation, import, storage and the form.</summary>
public class ShortAnswerQuestionTests
{
    private const string Owner = "teacher-1";

    // ---------- ShortAnswerMatcher ----------

    [Theory]
    [InlineData("София", "София")]
    [InlineData("София", "  софия  ")]                 // case and outer spaces
    [InlineData("Стара планина", "стара   планина")]   // repeated inner spaces
    [InlineData("кот", "кoт")]                          // Latin "o" in a Cyrillic word
    [InlineData("КОТ", "KOT")]                          // Latin capitals K, O, T
    [InlineData("Bob", "bob")]                          // Latin only, case-insensitive
    [InlineData("сос", "coc")]                          // every letter a look-alike
    [InlineData("йод", "йод")]              // composed (й) vs decomposed (и + combining breve)
    public void Matcher_CaseInsensitive_Matches(string accepted, string typed)
    {
        Assert.True(ShortAnswerMatcher.Matches(accepted, typed, caseSensitive: false));
    }

    [Theory]
    [InlineData("София", "Пловдив")]
    [InlineData("София", "Софияя")]
    [InlineData("София", "")]
    [InlineData("София", "   ")]
    [InlineData("", "София")]
    [InlineData("кот", "кит")]
    public void Matcher_DifferentText_DoesNotMatch(string accepted, string typed)
    {
        Assert.False(ShortAnswerMatcher.Matches(accepted, typed, caseSensitive: false));
    }

    [Fact]
    public void Matcher_CaseSensitive_RequiresTheSameCase()
    {
        Assert.True(ShortAnswerMatcher.Matches("NaCl", "NaCl", caseSensitive: true));
        Assert.False(ShortAnswerMatcher.Matches("NaCl", "nacl", caseSensitive: true));
        Assert.False(ShortAnswerMatcher.Matches("NaCl", "NACL", caseSensitive: true));
    }

    [Fact]
    public void Matcher_CaseSensitive_StillUnifiesLookAlikes()
    {
        Assert.True(ShortAnswerMatcher.Matches("Око", "Oкo", caseSensitive: true));    // Latin O and o
        Assert.False(ShortAnswerMatcher.Matches("Око", "oкo", caseSensitive: true));   // wrong case of the first letter
    }

    [Theory]
    [InlineData("*София*", "град София", true)]
    [InlineData("*София*", "София е столица", true)]
    [InlineData("*София*", "София", true)]               // "*" also matches nothing
    [InlineData("Со*я", "София", true)]
    [InlineData("Со*я", "Сокол", false)]
    [InlineData("*", "всичко", true)]
    [InlineData("2*3", "2 по 3", true)]
    [InlineData("a.b", "axb", false)]                   // regex characters are literal
    [InlineData("(1+2)", "(1+2)", true)]
    [InlineData(@"2\*3", "2*3", true)]                  // \* is a literal asterisk
    [InlineData(@"2\*3", "2x3", false)]
    public void Matcher_Wildcards(string accepted, string typed, bool expected)
    {
        Assert.Equal(expected, ShortAnswerMatcher.Matches(accepted, typed, caseSensitive: false));
    }

    [Fact]
    public void Matcher_ManyWildcardsOnLongText_DoNotHang()
    {
        var accepted = string.Concat(Enumerable.Repeat("*а", 30)) + "я";
        var typed = new string('а', 3000);

        // Either answer is fine; it must just return quickly instead of hanging the request
        var started = DateTime.UtcNow;
        ShortAnswerMatcher.Matches(accepted, typed, caseSensitive: false);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    // ---------- ShortAnswerGrader ----------

    private static Question Question(bool caseSensitive = false, params (string Text, decimal Fraction)[] accepted) => new()
    {
        Id = 3,
        Type = QuestionType.ShortAnswer,
        Points = 2,
        CaseSensitive = caseSensitive,
        Options = (accepted.Length > 0 ? accepted : new[] { (Text: "София", Fraction: 100m) })
            .Select((a, i) => new AnswerOption { Id = i + 1, Text = a.Text, Fraction = a.Fraction, Order = i + 1 })
            .ToList()
    };

    private static QuestionGrade Grade(Question question, string? typed)
        => new ShortAnswerGrader().Grade(question, SubmittedAnswer.Typed(typed));

    [Fact]
    public void Grader_IsRegistered()
    {
        Assert.IsType<ShortAnswerGrader>(QuestionGraders.For(QuestionType.ShortAnswer));
    }

    [Fact]
    public void Grader_Match_EarnsAllPoints_AndKeepsTheText()
    {
        var grade = Grade(Question(), "  софия ");

        Assert.Equal(2m, grade.AwardedPoints);
        Assert.True(grade.IsCorrect);
        Assert.Equal("софия", grade.Answer.Text);
    }

    [Fact]
    public void Grader_WrongText_EarnsNothing_ButKeepsTheText()
    {
        var grade = Grade(Question(), "Пловдив");

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
        Assert.Equal("Пловдив", grade.Answer.Text);
    }

    [Fact]
    public void Grader_Blank_IsUnanswered()
    {
        var grade = Grade(Question(), "   ");

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.Same(SubmittedAnswer.Empty, grade.Answer);
    }

    [Fact]
    public void Grader_TheBestMatchingAnswerCounts_WhateverTheOrder()
    {
        // A catch-all at 0% listed first must not hide the full-credit answer
        var question = Question(false, ("*", 0m), ("*град*", 50m), ("София", 100m));

        Assert.Equal(2m, Grade(question, "София").AwardedPoints);
        Assert.Equal(1m, Grade(question, "град София").AwardedPoints);
        Assert.False(Grade(question, "град София").IsCorrect);
        Assert.Equal(0m, Grade(question, "Варна").AwardedPoints);
    }

    [Fact]
    public void Grader_NegativeShare_CountsAsZero()
    {
        var question = Question(false, ("София", 100m), ("Пловдив", -50m));

        Assert.Equal(0m, Grade(question, "Пловдив").AwardedPoints);
    }

    [Fact]
    public void Grader_CaseSensitiveQuestion_RejectsTheWrongCase()
    {
        var question = Question(true, ("H2O", 100m));

        Assert.True(Grade(question, "H2O").IsCorrect);
        Assert.False(Grade(question, "h2o").IsCorrect);
    }

    [Fact]
    public void Grader_IgnoresAnOptionIdSentWithTheText()
    {
        var grade = new ShortAnswerGrader().Grade(Question(), new SubmittedAnswer(OptionId: 1, Text: "Варна"));

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.Null(grade.Answer.OptionId);
    }

    // ---------- SubmittedAnswer.Typed ----------

    [Fact]
    public void Typed_TrimsAndCutsLongText()
    {
        Assert.Equal("отговор", SubmittedAnswer.Typed("  отговор ").Text);
        Assert.Same(SubmittedAnswer.Empty, SubmittedAnswer.Typed(null));
        Assert.Same(SubmittedAnswer.Empty, SubmittedAnswer.Typed(" "));
        Assert.Equal(SubmittedAnswer.MaxTextLength, SubmittedAnswer.Typed(new string('а', 5000)).Text!.Length);
    }

    [Fact]
    public void FromForm_TypedAnswersAreIncluded()
    {
        var answers = SubmittedAnswer.FromForm(
            new Dictionary<int, int?> { [1] = 10 }, null,
            new Dictionary<int, string?> { [2] = " София ", [3] = "" });

        Assert.Equal(10, answers[1].OptionId);
        Assert.Equal("София", answers[2].Text);
        Assert.Same(SubmittedAnswer.Empty, answers[3]);
    }

    // ---------- Validation ----------

    [Fact]
    public void Input_ShortAnswer_GivesEveryAcceptedAnswerFullCredit()
    {
        var input = QuestionInput.ShortAnswer("Столица?", new[] { "София", "гр. София" }, caseSensitive: true, points: 3);

        Assert.Equal(QuestionType.ShortAnswer, input.Type);
        Assert.Equal(new[] { 100m, 100m }, input.Fractions);
        Assert.True(input.CaseSensitive);
        Assert.Equal(3, input.Points);
        Assert.Null(QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_OneAcceptedAnswer_IsEnough()
    {
        Assert.Null(QuizService.ValidateQuestion(QuestionInput.ShortAnswer("Столица?", new[] { "София" })));
    }

    [Fact]
    public void Validate_NoAcceptedAnswer_IsRejected()
    {
        Assert.Equal("Добавете поне един приемлив отговор.",
            QuizService.ValidateQuestion(QuestionInput.ShortAnswer("Столица?", Array.Empty<string>())));
    }

    [Fact]
    public void Validate_NoFullCreditAnswer_IsRejected()
    {
        var input = new QuestionInput("Столица?", new[] { "София" }, -1, 1, QuestionType.ShortAnswer, new[] { 50m });

        Assert.Equal("Поне един приемлив отговор трябва да носи всички точки (100%).", QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_ChoiceQuestions_StillNeedTwoOptions()
    {
        Assert.Equal("Добавете поне 2 отговора.",
            QuizService.ValidateQuestion(new QuestionInput("2 + 2?", new[] { "4" }, 0)));
    }

    // ---------- Import ----------

    [Fact]
    public void Parse_ShortAnswer_KeepsTheAcceptedAnswersSharesAndCase()
    {
        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"shortanswer\">" +
                  "<questiontext><text><![CDATA[<p>Химичната формула на водата?</p>]]></text></questiontext>" +
                  "<usecase>1</usecase>" +
                  "<answer fraction=\"100\"><text>H2O</text></answer>" +
                  "<answer fraction=\"50\"><text>*H2O*</text></answer>" +
                  "<answer fraction=\"0\"><text></text></answer></question></quiz>";

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Errors);
        var q = Assert.Single(result.Questions);
        Assert.Equal(QuestionType.ShortAnswer, q.Type);
        Assert.Equal("Химичната формула на водата?", q.Text);
        Assert.Equal(new[] { "H2O", "*H2O*" }, q.Options);   // the blank answer is dropped
        Assert.Equal(new[] { 100m, 50m }, q.Fractions);
        Assert.True(q.CaseSensitive);
        Assert.Null(QuizService.ValidateQuestion(q));
    }

    [Fact]
    public void Parse_ShortAnswer_WithoutUseCase_IsCaseInsensitive()
    {
        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"shortanswer\">" +
                  "<questiontext><text>Столица?</text></questiontext>" +
                  "<answer fraction=\"100\"><text>София</text></answer></question></quiz>";

        Assert.False(Assert.Single(MoodleXmlParser.Parse(xml).Questions).CaseSensitive);
    }

    [Fact]
    public void Parse_ShortAnswer_WithoutAFullCreditAnswer_IsReported()
    {
        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"shortanswer\"><name><text>КО</text></name>" +
                  "<questiontext><text>Столица?</text></questiontext>" +
                  "<answer fraction=\"50\"><text>София</text></answer></question></quiz>";

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Questions);
        Assert.Contains("„КО“: няма приемлив отговор за всички точки (100%).", result.Errors);
    }

    // ---------- Storage and results ----------

    [Fact]
    public async Task AddQuestionAsync_StoresTheAcceptedAnswersAndTheCaseFlag()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Упражнение", null, QuizMode.Practice, null, null), Owner)).Value!;

        await service.AddQuestionAsync(quiz.Id, QuestionInput.ShortAnswer("Формула?", new[] { "H2O" }, true, 2), Owner, false);

        var saved = (await service.GetByIdAsync(quiz.Id))!.Questions.Single();
        Assert.Equal(QuestionType.ShortAnswer, saved.Type);
        Assert.True(saved.CaseSensitive);
        Assert.Equal("H2O", saved.Options.Single().Text);
        Assert.Equal(100m, saved.Options.Single().Fraction);
    }

    [Fact]
    public async Task UpdateQuestionAsync_ToAChoiceType_ClearsTheCaseFlag()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Упражнение", null, QuizMode.Practice, null, null), Owner)).Value!;
        var question = (await service.AddQuestionAsync(quiz.Id, QuestionInput.ShortAnswer("Формула?", new[] { "H2O" }, true), Owner, false)).Value!;

        await service.UpdateQuestionAsync(question.Id, new QuestionInput("Формула?", new[] { "H2O", "CO2" }, 0), Owner, false);

        Assert.False((await service.GetQuestionAsync(question.Id))!.CaseSensitive);
    }

    [Fact]
    public async Task SubmitExamAsync_StoresTheTypedTextAndRebuildsTheResult()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id, QuestionInput.ShortAnswer("Столица?", new[] { "София" }, false, 2), Owner, false);
        await service.AddQuestionAsync(quiz.Id, QuestionInput.ShortAnswer("Най-дълга река?", new[] { "Дунав" }), Owner, false);
        await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        var exam = (await service.GetByIdAsync(quiz.Id))!;
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var questions = (await service.GetExamAttemptAsync(attempt.Token))!.Questions.Select(aq => aq.Question).ToList();
        var capital = questions.Single(q => q.Text == "Столица?");
        var river = questions.Single(q => q.Text == "Най-дълга река?");

        var result = await service.SubmitExamAsync(attempt.Token, SubmittedAnswer.FromForm(null, null,
            new Dictionary<int, string?> { [capital.Id] = "софия", [river.Id] = "Искър" }));

        Assert.Equal(2m, result.Value!.Score);
        Assert.Equal(3m, result.Value.MaxScore);

        var stored = await context.AttemptAnswers.Where(a => a.AttemptId == attempt.Id).ToListAsync();
        Assert.Equal("софия", stored.Single(a => a.QuestionId == capital.Id).ResponseText);
        Assert.Equal("Искър", stored.Single(a => a.QuestionId == river.Id).ResponseText);
        Assert.All(stored, a => Assert.Null(a.SelectedOptionId));

        var rebuilt = (await service.GetExamResultAsync(attempt.Token)).Value!;
        Assert.Equal(2m, rebuilt.Score);
        Assert.Equal("Искър", rebuilt.Questions.Single(r => r.Question.Id == river.Id).TypedText);
        Assert.False(rebuilt.Questions.Single(r => r.Question.Id == river.Id).IsCorrect);
    }

    // ---------- The question form ----------

    [Fact]
    public void Form_ShortAnswer_MapsTheAcceptedAnswers()
    {
        var form = new QuestionFormViewModel
        {
            Type = QuestionType.ShortAnswer,
            Text = "Столица?",
            Options = new List<string> { " София ", "", "гр. София" },
            CorrectIndex = 0,
            CorrectIndexes = new List<int> { 2 },
            CaseSensitive = true,
            Points = 2
        };

        var input = form.ToInput();

        Assert.Equal(QuestionType.ShortAnswer, input.Type);
        Assert.Equal(new[] { "София", "гр. София" }, input.Options);
        Assert.Equal(new[] { 100m, 100m }, input.Fractions);
        Assert.True(input.CaseSensitive);
        Assert.Equal(2, input.Points);
    }

    [Fact]
    public void Form_CaseFlag_IsIgnoredForChoiceQuestions()
    {
        var form = new QuestionFormViewModel { Text = "В", Options = new List<string> { "а", "б" }, CorrectIndex = 0, CaseSensitive = true };

        var input = form.ToInput();

        Assert.Equal(QuestionType.SingleChoice, input.Type);
        Assert.False(input.CaseSensitive);
    }

    [Fact]
    public void Form_FromShortAnswerQuestion_FillsTheAnswersAndTheCaseFlag()
    {
        var form = QuestionFormViewModel.FromQuestion(Question(true, ("София", 100m), ("гр. София", 100m)));

        Assert.Equal(QuestionType.ShortAnswer, form.Type);
        Assert.Equal(new[] { "София", "гр. София" }, form.Options);
        Assert.True(form.CaseSensitive);
        Assert.False(form.HasCustomShares);
    }

    [Fact]
    public void Form_FromImportedPartialAnswers_WarnsThatSavingReplacesThem()
    {
        var form = QuestionFormViewModel.FromQuestion(Question(false, ("София", 100m), ("*София*", 50m)));

        Assert.True(form.HasCustomShares);
    }
}
