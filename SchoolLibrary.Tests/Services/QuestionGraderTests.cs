using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class QuestionGraderTests
{
    // A single-choice question with options 10 (full credit), 11 (wrong) and 12 (the given share)
    private static Question Question(int points = 2, decimal thirdFraction = 50m) => new()
    {
        Id = 1,
        Type = QuestionType.SingleChoice,
        Points = points,
        Options = new List<AnswerOption>
        {
            new() { Id = 10, Text = "верен", Fraction = 100m },
            new() { Id = 11, Text = "грешен", Fraction = 0m },
            new() { Id = 12, Text = "частично", Fraction = thirdFraction }
        }
    };

    private static readonly SingleChoiceGrader Grader = new();

    // ---------- SingleChoiceGrader ----------

    [Fact]
    public void SingleChoice_CorrectOption_EarnsAllPoints()
    {
        var grade = Grader.Grade(Question(), SubmittedAnswer.Choice(10));

        Assert.Equal(2m, grade.AwardedPoints);
        Assert.True(grade.IsCorrect);
        Assert.Equal(10, grade.Answer.OptionId);
    }

    [Fact]
    public void SingleChoice_WrongOption_EarnsNothing()
    {
        var grade = Grader.Grade(Question(), SubmittedAnswer.Choice(11));

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
        Assert.Equal(11, grade.Answer.OptionId);
    }

    [Fact]
    public void SingleChoice_PartialOption_EarnsItsShare()
    {
        var grade = Grader.Grade(Question(points: 3, thirdFraction: 50m), SubmittedAnswer.Choice(12));

        Assert.Equal(1.5m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
    }

    [Fact]
    public void SingleChoice_Share_IsRoundedToTwoDecimals()
    {
        // 3 × 33.33% = 0.9999 → 1.00
        var grade = Grader.Grade(Question(points: 3, thirdFraction: 33.33m), SubmittedAnswer.Choice(12));

        Assert.Equal(1m, grade.AwardedPoints);
    }

    [Fact]
    public void SingleChoice_NegativeShare_NeverTakesPointsAway()
    {
        var grade = Grader.Grade(Question(thirdFraction: -50m), SubmittedAnswer.Choice(12));

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
    }

    [Fact]
    public void SingleChoice_Unanswered_EarnsNothing()
    {
        var grade = Grader.Grade(Question(), SubmittedAnswer.Empty);

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
        Assert.Null(grade.Answer.OptionId);
    }

    [Fact]
    public void SingleChoice_OptionOfAnotherQuestion_IsTreatedAsUnanswered()
    {
        var grade = Grader.Grade(Question(), SubmittedAnswer.Choice(999));

        Assert.Equal(0m, grade.AwardedPoints);
        Assert.False(grade.IsCorrect);
        Assert.Same(SubmittedAnswer.Empty, grade.Answer);
    }

    // ---------- QuestionGraders ----------

    [Fact]
    public void Graders_FindTheSingleChoiceGrader()
    {
        Assert.IsType<SingleChoiceGrader>(QuestionGraders.For(QuestionType.SingleChoice));
    }

    [Fact]
    public void Graders_EveryQuestionTypeHasAGrader()
    {
        // Code questions are run on the sandbox server, so they are graded asynchronously, not here
        foreach (var type in Enum.GetValues<QuestionType>().Where(t => t != QuestionType.Code))
        {
            Assert.Equal(type, QuestionGraders.For(type).Type);
        }
    }

    [Fact]
    public void Graders_CodeQuestion_HasNoSynchronousGrader()
    {
        Assert.Throws<NotSupportedException>(() => QuestionGraders.For(QuestionType.Code));
    }

    [Fact]
    public void Graders_UnknownType_Throws()
    {
        Assert.Throws<NotSupportedException>(() => QuestionGraders.For((QuestionType)999));
    }

    // ---------- SubmittedAnswer ----------

    [Fact]
    public void Choice_WithoutAnOption_IsEmpty()
    {
        Assert.Same(SubmittedAnswer.Empty, SubmittedAnswer.Choice(null));
    }

    [Fact]
    public void FromChoices_WrapsEachPick()
    {
        var answers = SubmittedAnswer.FromChoices(new Dictionary<int, int?> { [1] = 10, [2] = null });

        Assert.Equal(10, answers[1].OptionId);
        Assert.Same(SubmittedAnswer.Empty, answers[2]);
    }

    [Fact]
    public void FromChoices_Null_IsNoAnswers()
    {
        Assert.Empty(SubmittedAnswer.FromChoices(null));
    }

    // ---------- QuizGrading with partial credit ----------

    [Fact]
    public void Grading_SumsTheAwardedPoints()
    {
        var quiz = new Quiz
        {
            Questions = new List<Question>
            {
                Question(points: 2),                                   // correct → 2
                WithId(Question(points: 3, thirdFraction: 50m), 2)     // half → 1.5
            }
        };
        var answers = new Dictionary<int, SubmittedAnswer>
        {
            [1] = SubmittedAnswer.Choice(10),
            [2] = SubmittedAnswer.Choice(12)
        };

        var result = QuizGrading.Grade(quiz, answers);

        Assert.Equal(3.5m, result.Score);
        Assert.Equal(5m, result.MaxScore);
        Assert.Equal(new[] { 2m, 1.5m }, result.Questions.Select(q => q.AwardedPoints));
        Assert.Equal(10, result.Questions[1].CorrectOptionId);
        Assert.Equal(12, result.Questions[1].SelectedOptionId);
    }

    private static Question WithId(Question question, int id)
    {
        question.Id = id;
        return question;
    }
}
