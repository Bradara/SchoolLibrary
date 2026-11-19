using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Web;

public class QuizManagementControllerTests
{
    private readonly Mock<IQuizService> _service = new();

    private QuizManagementController Controller(string userId, params string[] roles) =>
        new QuizManagementController(_service.Object).WithUser(userId, roles);

    private static PagedResult<QuizListItem> EmptyPage() =>
        new(new List<QuizListItem>(), totalCount: 0, page: 1, pageSize: 10);

    [Fact]
    public async Task Index_ForATeacher_ListsOnlyTheirQuizzes()
    {
        _service.Setup(s => s.GetManagedAsync("t1", It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(EmptyPage());

        var result = await Controller("t1", Roles.Teacher).Index();

        Assert.IsType<ViewResult>(result);
        _service.Verify(s => s.GetManagedAsync("t1", 1, It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task Index_ForAnAdministrator_ListsEveryQuiz()
    {
        _service.Setup(s => s.GetManagedAsync(null, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(EmptyPage());

        var result = await Controller("admin", Roles.Administrator).Index();

        Assert.IsType<ViewResult>(result);
        _service.Verify(s => s.GetManagedAsync(null, 1, It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task Attempts_Success_ShowsTheReview()
    {
        var review = new QuizReview(
            new Quiz { Id = 5, Title = "Изпит" },
            new PagedResult<QuizAttempt>(new List<QuizAttempt>(), 0, 1, 20));
        _service.Setup(s => s.GetReviewAsync(5, "t1", false, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(ServiceResult<QuizReview>.Success(review));

        var result = await Controller("t1", Roles.Teacher).Attempts(5);

        Assert.Same(review, Assert.IsType<ViewResult>(result).Model);
    }

    [Fact]
    public async Task Attempts_Missing_ReturnsNotFound()
    {
        _service.Setup(s => s.GetReviewAsync(5, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(ServiceResult<QuizReview>.Failure(ServiceError.NotFound, "Тестът не е намерен."));

        Assert.IsType<NotFoundResult>(await Controller("t1", Roles.Teacher).Attempts(5));
    }

    [Fact]
    public async Task Attempts_ForSomeoneElsesQuiz_ReturnsForbid()
    {
        _service.Setup(s => s.GetReviewAsync(5, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(ServiceResult<QuizReview>.Failure(ServiceError.Forbidden, "Нямате права."));

        Assert.IsType<ForbidResult>(await Controller("t1", Roles.Teacher).Attempts(5));
    }
}
