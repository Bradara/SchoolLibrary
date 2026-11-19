using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Web;

public class QuizzesControllerTests
{
    private readonly Mock<IQuizService> _service = new();

    // Practice is open, so the tests act as an anonymous visitor
    private QuizzesController Controller() => new QuizzesController(_service.Object).WithUser(null);

    [Fact]
    public async Task Index_ShowsThePageOfPublishedPracticeQuizzes()
    {
        var items = new List<QuizListItem>
        {
            new(new Quiz { Id = 1, Title = "А" }, 3, 0),
            new(new Quiz { Id = 2, Title = "Б" }, 5, 0)
        };
        _service.Setup(s => s.GetPublishedPracticeAsync(2, It.IsAny<int>()))
            .ReturnsAsync(new PagedResult<QuizListItem>(items, totalCount: 12, page: 2, pageSize: 10));

        var result = await Controller().Index(page: 2);

        var model = Assert.IsType<PagedResult<QuizListItem>>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(12, model.TotalCount);
        Assert.Equal(2, model.Items.Count);
    }

    [Fact]
    public async Task Solve_Get_ReturnsTheQuiz()
    {
        var quiz = new Quiz { Id = 5, Title = "Дроби" };
        _service.Setup(s => s.GetPracticeForSolvingAsync(5)).ReturnsAsync(quiz);

        var result = await Controller().Solve(5);

        Assert.Same(quiz, Assert.IsType<ViewResult>(result).Model);
    }

    [Fact]
    public async Task Solve_Get_Missing_ReturnsNotFound()
    {
        _service.Setup(s => s.GetPracticeForSolvingAsync(5)).ReturnsAsync((Quiz?)null);

        Assert.IsType<NotFoundResult>(await Controller().Solve(5));
    }

    [Fact]
    public async Task Solve_Post_GradesTheAnswersAndShowsTheResult()
    {
        var graded = new QuizResult(new Quiz { Id = 5, Title = "Дроби" }, 1, 2, new List<QuestionResult>());
        _service.Setup(s => s.GradePracticeAsync(5, It.IsAny<IReadOnlyDictionary<int, int?>>()))
            .ReturnsAsync(ServiceResult<QuizResult>.Success(graded));

        var result = await Controller().Solve(5, new Dictionary<int, int?> { [1] = 2 });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Result", view.ViewName);
        Assert.Same(graded, view.Model);
    }

    [Fact]
    public async Task Solve_Post_WithNoAnswers_GradesWithAnEmptySet()
    {
        var graded = new QuizResult(new Quiz { Id = 5 }, 0, 2, new List<QuestionResult>());
        _service.Setup(s => s.GradePracticeAsync(5, It.IsAny<IReadOnlyDictionary<int, int?>>()))
            .ReturnsAsync(ServiceResult<QuizResult>.Success(graded));

        var result = await Controller().Solve(5, null);

        Assert.IsType<ViewResult>(result);
        _service.Verify(s => s.GradePracticeAsync(5, It.Is<IReadOnlyDictionary<int, int?>>(d => d.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task Solve_Post_Missing_ReturnsNotFound()
    {
        _service.Setup(s => s.GradePracticeAsync(5, It.IsAny<IReadOnlyDictionary<int, int?>>()))
            .ReturnsAsync(ServiceResult<QuizResult>.Failure(ServiceError.NotFound, "Упражнението не е намерено."));

        Assert.IsType<NotFoundResult>(await Controller().Solve(5, new Dictionary<int, int?>()));
    }
}
