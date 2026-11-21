using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Services;

/// <summary>Matching questions end to end: the two sides, grading, validation, import, storage and the form.</summary>
public class MatchingQuestionTests
{
    private const string Owner = "teacher-1";

    // Items 1–3 with their answers; option 4 is an extra wrong answer (no item). Two items share "метал".
    private static Question Question() => new()
    {
        Id = 9,
        Type = QuestionType.Matching,
        Points = 3,
        Options = new List<AnswerOption>
        {
            new() { Id = 1, Text = "Желязо", MatchText = "метал", Order = 1 },
            new() { Id = 2, Text = "Мед", MatchText = "метал", Order = 2 },
            new() { Id = 3, Text = "Сяра", MatchText = "неметал", Order = 3 },
            new() { Id = 4, Text = "", MatchText = "благороден газ", Order = 4 }
        }
    };

    private static QuestionGrade Grade(Question question, Dictionary<int, string?> pairs)
        => new MatchingGrader().Grade(question, SubmittedAnswer.Matched(pairs));

    // ---------- MatchingAnswers ----------

    [Fact]
    public void Items_AreTheOptionsWithText()
    {
        Assert.Equal(new[] { 1, 2, 3 }, MatchingAnswers.Items(Question()).Select(o => o.Id));
    }

    [Fact]
    public void Choices_AreEachAnswerOnce_Alphabetically_IncludingTheExtraOnes()
    {
        Assert.Equal(new[] { "благороден газ", "метал", "неметал" }, MatchingAnswers.Choices(Question()));
    }

    [Fact]
    public void Choices_DoNotDependOnTheOptionOrder()
    {
        var shuffled = Question();
        shuffled.Options = shuffled.Options.Reverse().ToList();

        Assert.Equal(MatchingAnswers.Choices(Question()), MatchingAnswers.Choices(shuffled));
    }

    [Fact]
    public void OptionFor_FindsTheFirstOptionWithTheAnswer()
    {
        Assert.Equal(1, MatchingAnswers.OptionFor(Question(), "метал")!.Id);
        Assert.Equal(4, MatchingAnswers.OptionFor(Question(), "благороден газ")!.Id);
        Assert.Null(MatchingAnswers.OptionFor(Question(), "вода"));
        Assert.Null(MatchingAnswers.OptionFor(Question(), null));
    }

    // ---------- MatchingGrader ----------

    [Fact]
    public void Grader_IsRegistered()
    {
        Assert.IsType<MatchingGrader>(QuestionGraders.For(QuestionType.Matching));
    }

    [Fact]
    public void Grader_AllPairsRight_EarnsAllPoints()
    {
        var grade = Grade(Question(), new() { [1] = "метал", [2] = "метал", [3] = "неметал" });

        Assert.Equal(3m, grade.AwardedPoints);
        Assert.True(grade.IsCorrect);
    }

    [Fact]
    public void Grader_EachRightPair_EarnsItsShare()
    {
        var grade = Grade(Question(), new() { [1] = "метал", [2] = "неметал", [3] = "благороден газ" });

        Assert.Equal(1m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
        Assert.Equal("неметал", grade.Answer.MatchedPairs[2]);
    }

    [Fact]
    public void Grader_SharesAreRounded()
    {
        var question = Question();
        question.Points = 1;

        Assert.Equal(0.67m, Grade(question, new() { [1] = "метал", [2] = "метал" }).AwardedPoints);
    }

    [Fact]
    public void Grader_DropsUnknownItemsAndAnswers()
    {
        // Item 4 is not an item (extra answer), 99 is not in the question, "вода" is not in the list
        var grade = Grade(Question(), new() { [1] = "метал", [4] = "метал", [99] = "метал", [3] = "вода" });

        Assert.Equal(1m, grade.AwardedPoints);
        Assert.Equal(new[] { 1 }, grade.Answer.MatchedPairs.Keys);
    }

    [Fact]
    public void Grader_NothingPicked_IsUnanswered()
    {
        Assert.Same(SubmittedAnswer.Empty, Grade(Question(), new() { [1] = "", [2] = null }).Answer);
        Assert.Same(SubmittedAnswer.Empty, new MatchingGrader().Grade(Question(), SubmittedAnswer.Empty).Answer);
    }

    [Fact]
    public void Grader_AnswersAreComparedExactly()
    {
        Assert.Equal(0m, Grade(Question(), new() { [3] = "Неметал" }).AwardedPoints);
    }

    // ---------- SubmittedAnswer ----------

    [Fact]
    public void Matched_DropsBlankPicksAndTrims()
    {
        var answer = SubmittedAnswer.Matched(new Dictionary<int, string?> { [1] = " метал ", [2] = " ", [3] = null });

        Assert.Equal(new Dictionary<int, string> { [1] = "метал" }, answer.MatchedPairs);
        Assert.Same(SubmittedAnswer.Empty, SubmittedAnswer.Matched(null));
    }

    [Fact]
    public void FromForm_IncludesTheMatchingPicks()
    {
        var answers = SubmittedAnswer.FromForm(null, null, null,
            new Dictionary<int, Dictionary<int, string?>> { [9] = new() { [1] = "метал" } });

        Assert.Equal("метал", answers[9].MatchedPairs[1]);
    }

    // ---------- Validation ----------

    [Fact]
    public void Input_Matching_KeepsItemsAndAnswersSideBySide()
    {
        var input = QuestionInput.Matching("Свържи:", new[] { ("Желязо", "метал"), ("Сяра", "неметал"), ("", "газ") }, 2);

        Assert.Equal(QuestionType.Matching, input.Type);
        Assert.Equal(new[] { "Желязо", "Сяра", "" }, input.Options);
        Assert.Equal(new[] { "метал", "неметал", "газ" }, input.Matches);
        Assert.Null(input.Fractions);
        Assert.Null(QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_FewerThanTwoItems_IsRejected()
    {
        var input = QuestionInput.Matching("Свържи:", new[] { ("Желязо", "метал"), ("", "газ") });

        Assert.Equal("Добавете поне 2 двойки за съпоставяне.", QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_ItemWithoutAnAnswer_IsRejected()
    {
        var input = QuestionInput.Matching("Свържи:", new[] { ("Желязо", "метал"), ("Сяра", " ") });

        Assert.Equal("Всеки ред трябва да има отговор вдясно.", QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_RepeatedItem_IsRejected()
    {
        var input = QuestionInput.Matching("Свържи:", new[] { ("Желязо", "метал"), ("желязо", "неметал") });

        Assert.Equal("Всеки ляв елемент трябва да е различен.", QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_MissingAnswersList_IsRejected()
    {
        var input = new QuestionInput("Свържи:", new[] { "а", "б" }, -1, 1, QuestionType.Matching);

        Assert.Equal("Всеки ред трябва да има отговор вдясно.", QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Validate_Matching_StillChecksThePoints()
    {
        var input = QuestionInput.Matching("Свържи:", new[] { ("а", "1"), ("б", "2") }, 0);

        Assert.Equal("Точките за въпрос трябва да са между 1 и 100.", QuizService.ValidateQuestion(input));
    }

    // ---------- Import ----------

    [Fact]
    public void Parse_Matching_ReadsThePairsAndTheExtraAnswers()
    {
        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"matching\">" +
                  "<questiontext><text><![CDATA[<p>Свържете елемента с вида му.</p>]]></text></questiontext>" +
                  "<subquestion format=\"html\"><text><![CDATA[<p>Желязо</p>]]></text><answer><text>метал</text></answer></subquestion>" +
                  "<subquestion format=\"html\"><text>Сяра</text><answer><text>неметал</text></answer></subquestion>" +
                  "<subquestion format=\"html\"><text></text><answer><text>благороден газ</text></answer></subquestion>" +
                  "<subquestion format=\"html\"><text>Празен</text><answer><text></text></answer></subquestion>" +
                  "</question></quiz>";

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Errors);
        var q = Assert.Single(result.Questions);
        Assert.Equal(QuestionType.Matching, q.Type);
        Assert.Equal("Свържете елемента с вида му.", q.Text);
        Assert.Equal(new[] { "Желязо", "Сяра", "" }, q.Options);   // the row without an answer is dropped
        Assert.Equal(new[] { "метал", "неметал", "благороден газ" }, q.Matches);
        Assert.Null(QuizService.ValidateQuestion(q));
    }

    [Fact]
    public void Parse_MatchingWithOnePair_IsReported()
    {
        var xml = "<?xml version=\"1.0\"?><quiz><question type=\"matching\"><name><text>С</text></name>" +
                  "<questiontext><text>Свържи</text></questiontext>" +
                  "<subquestion><text>а</text><answer><text>1</text></answer></subquestion></question></quiz>";

        var result = MoodleXmlParser.Parse(xml);

        Assert.Empty(result.Questions);
        Assert.Contains("„С“: нужни са поне 2 двойки за съпоставяне.", result.Errors);
    }

    // ---------- Storage and results ----------

    private static async Task<(QuizService Service, Quiz Exam)> ExamAsync(Data.ApplicationDbContext context)
    {
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Изпит", null, QuizMode.Exam, 30, null), Owner)).Value!;
        await service.AddQuestionAsync(quiz.Id, QuestionInput.Matching("Свържи:",
            new[] { ("Желязо", "метал"), ("Мед", "метал"), ("Сяра", "неметал"), ("", "благороден газ") }, 3), Owner, false);
        await service.SetPublishedAsync(quiz.Id, true, Owner, false);
        return (service, (await service.GetByIdAsync(quiz.Id))!);
    }

    [Fact]
    public async Task AddQuestionAsync_StoresBothSides()
    {
        using var context = TestDbContextFactory.Create();
        var (_, exam) = await ExamAsync(context);

        var options = exam.Questions.Single().Options.OrderBy(o => o.Order).ToList();
        Assert.Equal(new[] { "Желязо", "Мед", "Сяра", "" }, options.Select(o => o.Text));
        Assert.Equal(new[] { "метал", "метал", "неметал", "благороден газ" }, options.Select(o => o.MatchText));
        Assert.All(options, o => Assert.Null(o.Tolerance));
    }

    [Fact]
    public async Task OtherQuestionTypes_HaveNoMatchText()
    {
        using var context = TestDbContextFactory.Create();
        var service = new QuizService(context, new QuizItemService(context));
        var quiz = (await service.CreateAsync(new QuizInput("Упражнение", null, QuizMode.Practice, null, null), Owner)).Value!;

        await service.AddQuestionAsync(quiz.Id, new QuestionInput("2 + 2?", new[] { "4", "5" }, 0), Owner, false);

        Assert.All((await service.GetByIdAsync(quiz.Id))!.Questions.Single().Options, o => Assert.Null(o.MatchText));
    }

    [Fact]
    public async Task SubmitExamAsync_StoresThePairsAndRebuildsTheResult()
    {
        using var context = TestDbContextFactory.Create();
        var (service, exam) = await ExamAsync(context);
        var attempt = (await service.StartExamAsync(exam.Code!, "Иван", "5")).Value!;
        var question = (await service.GetExamAttemptAsync(attempt.Token))!.Questions.Single().Question;
        var byItem = question.Options.Where(o => o.Text.Length > 0).ToDictionary(o => o.Text, o => o.Id);

        var result = await service.SubmitExamAsync(attempt.Token, SubmittedAnswer.FromForm(null, null, null,
            new Dictionary<int, Dictionary<int, string?>>
            {
                [question.Id] = new() { [byItem["Желязо"]] = "метал", [byItem["Мед"]] = "неметал", [byItem["Сяра"]] = "неметал" }
            }));

        Assert.Equal(2m, result.Value!.Score);

        var stored = await context.AttemptAnswers.Include(a => a.Choices).SingleAsync(a => a.AttemptId == attempt.Id);
        Assert.Equal(3, stored.Choices.Count);
        Assert.All(stored.Choices, c => Assert.NotNull(c.MatchedOptionId));
        Assert.Null(stored.SelectedOptionId);

        var rebuilt = (await service.GetExamResultAsync(attempt.Token)).Value!;
        Assert.Equal(2m, rebuilt.Score);
        var questionResult = rebuilt.Questions.Single();
        Assert.Equal("неметал", questionResult.PickedMatch(byItem["Мед"]));
        Assert.Equal("метал", questionResult.PickedMatch(byItem["Желязо"]));
        Assert.True(questionResult.IsPartial);
    }

    // ---------- The question form ----------

    [Fact]
    public void Form_Matching_KeepsExtraAnswersAndDropsEmptyRows()
    {
        var form = new QuestionFormViewModel
        {
            Type = QuestionType.Matching,
            Text = "Свържи:",
            Options = new List<string> { "Желязо", "", "", "Сяра" },
            Matches = new List<string> { "метал", "газ", "", "неметал" },
            Points = 2
        };

        var input = form.ToInput();

        Assert.Equal(QuestionType.Matching, input.Type);
        Assert.Equal(new[] { "Желязо", "", "Сяра" }, input.Options);
        Assert.Equal(new[] { "метал", "газ", "неметал" }, input.Matches);
        Assert.Equal(2, input.Points);
        Assert.Null(QuizService.ValidateQuestion(input));
    }

    [Fact]
    public void Form_FromMatchingQuestion_FillsBothSides()
    {
        var form = QuestionFormViewModel.FromQuestion(Question());

        Assert.Equal(QuestionType.Matching, form.Type);
        Assert.Equal(new[] { "Желязо", "Мед", "Сяра", "" }, form.Options);
        Assert.Equal(new[] { "метал", "метал", "неметал", "благороден газ" }, form.Matches);
    }
}
