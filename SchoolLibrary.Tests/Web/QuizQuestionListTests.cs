using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

// How the questions are grouped into screens for "one question per screen"
public class QuizQuestionListTests
{
    private static Question Q(int id, QuestionType type = QuestionType.SingleChoice)
        => new() { Id = id, Text = "Въпрос " + id, Type = type };

    private static QuizQuestionListViewModel List(params Question[] questions)
        => new() { Questions = questions, OneQuestionPerPage = true };

    [Fact]
    public void Screens_OneQuestionEach_NumberedInOrder()
    {
        var screens = List(Q(1), Q(2), Q(3)).Screens;

        Assert.Equal(new[] { 1, 2, 3 }, screens.Select(s => s.Number));
        Assert.Equal(new[] { 1, 2, 3 }, screens.Select(s => s.Question!.Id));
        Assert.All(screens, s => Assert.Empty(s.Descriptions));
    }

    [Fact]
    public void Screens_ADescription_GoesWithTheQuestionAfterIt_AndIsNotNumbered()
    {
        var screens = List(Q(1), Q(10, QuestionType.Description), Q(11, QuestionType.Description), Q(2)).Screens;

        Assert.Equal(2, screens.Count);
        Assert.Empty(screens[0].Descriptions);
        Assert.Equal(new[] { 10, 11 }, screens[1].Descriptions.Select(d => d.Id));
        Assert.Equal(2, screens[1].Number);
        Assert.Equal(2, screens[1].Question!.Id);
    }

    [Fact]
    public void Screens_DescriptionsAtTheEnd_GoWithTheLastQuestion()
    {
        var screens = List(Q(1), Q(2), Q(10, QuestionType.Description)).Screens;

        Assert.Equal(2, screens.Count);
        Assert.Equal(10, Assert.Single(screens[1].TrailingDescriptions).Id);
        Assert.Empty(screens[0].TrailingDescriptions);
    }

    [Fact]
    public void Screens_OnlyDescriptions_MakeOneScreenWithoutAQuestion()
    {
        var screen = Assert.Single(List(Q(10, QuestionType.Description)).Screens);

        Assert.Null(screen.Question);
        Assert.Equal(0, screen.Number);
        Assert.Single(screen.Descriptions);
    }

    [Fact]
    public void Screens_NoQuestions_IsEmpty()
    {
        Assert.Empty(List().Screens);
    }

    [Fact]
    public void QuizAttempt_OneQuestionPerPage_TheClassExamWins_OtherwiseTheQuiz()
    {
        Assert.True(new QuizAttempt { Quiz = new Quiz { OneQuestionPerPage = false }, ExamAssignment = new ExamAssignment { OneQuestionPerPage = true } }.OneQuestionPerPage);
        Assert.False(new QuizAttempt { Quiz = new Quiz { OneQuestionPerPage = true }, ExamAssignment = new ExamAssignment { OneQuestionPerPage = false } }.OneQuestionPerPage);
        Assert.True(new QuizAttempt { Quiz = new Quiz { OneQuestionPerPage = true } }.OneQuestionPerPage);
        Assert.False(new QuizAttempt { Quiz = new Quiz { OneQuestionPerPage = false } }.OneQuestionPerPage);
        Assert.False(new QuizAttempt().OneQuestionPerPage);
    }
}
