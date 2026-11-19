using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

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

    // ---------- Exam: entry by code ----------

    [Fact]
    public void Exam_Get_ShowsAnEmptyForm()
    {
        var model = Assert.IsType<ExamEntryViewModel>(Assert.IsType<ViewResult>(Controller().Exam()).Model);
        Assert.Equal(string.Empty, model.Code);
    }

    [Fact]
    public async Task Exam_Post_WithInvalidModel_RedisplaysTheForm()
    {
        var controller = Controller();
        controller.ModelState.AddModelError("Code", "задължително");

        var result = await controller.Exam(new ExamEntryViewModel());

        Assert.IsType<ViewResult>(result);
        _service.Verify(s => s.StartExamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Exam_Post_Success_RedirectsToTheAttempt()
    {
        var token = Guid.NewGuid();
        _service.Setup(s => s.StartExamAsync("ABC123", "Иван", "5"))
            .ReturnsAsync(ServiceResult<QuizAttempt>.Success(new QuizAttempt { Token = token }));

        var result = await Controller().Exam(new ExamEntryViewModel { Code = "ABC123", StudentName = "Иван", StudentNumber = "5" });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Take", redirect.ActionName);
        Assert.Equal(token, redirect.RouteValues!["token"]);
    }

    [Theory]
    [InlineData(ServiceError.NotFound)]
    [InlineData(ServiceError.Conflict)]
    public async Task Exam_Post_WhenStartFails_RedisplaysTheFormWithTheMessage(ServiceError error)
    {
        _service.Setup(s => s.StartExamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<QuizAttempt>.Failure(error, "грешка"));

        var controller = Controller();
        var result = await controller.Exam(new ExamEntryViewModel { Code = "X", StudentName = "Иван" });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
    }

    // ---------- Exam: taking ----------

    private static QuizAttempt AttemptInProgress(int? timeLimit = 30)
        => new()
        {
            Token = Guid.NewGuid(),
            StartedOn = DateTime.UtcNow,
            Quiz = new Quiz { Title = "Изпит", TimeLimitMinutes = timeLimit }
        };

    [Fact]
    public async Task Take_Get_InProgress_ShowsTheFormWithTimeLeft()
    {
        var attempt = AttemptInProgress(timeLimit: 30);
        _service.Setup(s => s.GetExamAttemptAsync(attempt.Token)).ReturnsAsync(attempt);

        var result = await Controller().Take(attempt.Token);

        var model = Assert.IsType<ExamTakeViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Same(attempt, model.Attempt);
        Assert.NotNull(model.RemainingSeconds);
        Assert.InRange(model.RemainingSeconds!.Value, 1, 30 * 60);
    }

    [Fact]
    public async Task Take_Get_NoTimeLimit_HasNoCountdown()
    {
        var attempt = AttemptInProgress(timeLimit: null);
        _service.Setup(s => s.GetExamAttemptAsync(attempt.Token)).ReturnsAsync(attempt);

        var result = await Controller().Take(attempt.Token);

        var model = Assert.IsType<ExamTakeViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Null(model.RemainingSeconds);
    }

    [Fact]
    public async Task Take_Get_AlreadySubmitted_RedirectsToTheResult()
    {
        var attempt = new QuizAttempt { Token = Guid.NewGuid(), SubmittedOn = DateTime.UtcNow, Quiz = new Quiz() };
        _service.Setup(s => s.GetExamAttemptAsync(attempt.Token)).ReturnsAsync(attempt);

        var result = await Controller().Take(attempt.Token);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ExamResult", redirect.ActionName);
    }

    [Fact]
    public async Task Take_Get_Unknown_ReturnsNotFound()
    {
        _service.Setup(s => s.GetExamAttemptAsync(It.IsAny<Guid>())).ReturnsAsync((QuizAttempt?)null);

        Assert.IsType<NotFoundResult>(await Controller().Take(Guid.NewGuid()));
    }

    [Fact]
    public async Task Take_Post_Submits_AndRedirectsToTheResult()
    {
        var token = Guid.NewGuid();
        _service.Setup(s => s.SubmitExamAsync(token, It.IsAny<IReadOnlyDictionary<int, int?>>()))
            .ReturnsAsync(ServiceResult<QuizResult>.Success(new QuizResult(new Quiz(), 1, 2, new List<QuestionResult>())));

        var result = await Controller().Take(token, new Dictionary<int, int?> { [1] = 2 });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ExamResult", redirect.ActionName);
        Assert.Equal(token, redirect.RouteValues!["token"]);
    }

    [Fact]
    public async Task Take_Post_Unknown_ReturnsNotFound()
    {
        _service.Setup(s => s.SubmitExamAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyDictionary<int, int?>>()))
            .ReturnsAsync(ServiceResult<QuizResult>.Failure(ServiceError.NotFound, "Опитът не е намерен."));

        Assert.IsType<NotFoundResult>(await Controller().Take(Guid.NewGuid(), null));
    }

    // ---------- Exam: result ----------

    [Fact]
    public async Task ExamResult_Submitted_ShowsTheResult()
    {
        var token = Guid.NewGuid();
        var graded = new QuizResult(new Quiz { Title = "Изпит" }, 2, 2, new List<QuestionResult>());
        _service.Setup(s => s.GetExamResultAsync(token)).ReturnsAsync(ServiceResult<QuizResult>.Success(graded));
        _service.Setup(s => s.GetExamAttemptAsync(token))
            .ReturnsAsync(new QuizAttempt { Token = token, StudentName = "Иван", SubmittedOn = DateTime.UtcNow });

        var result = await Controller().ExamResult(token);

        var model = Assert.IsType<ExamResultViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Same(graded, model.Result);
        Assert.Equal("Иван", model.StudentName);
    }

    [Fact]
    public async Task ExamResult_NotSubmittedYet_RedirectsBackToTake()
    {
        var token = Guid.NewGuid();
        _service.Setup(s => s.GetExamResultAsync(token))
            .ReturnsAsync(ServiceResult<QuizResult>.Failure(ServiceError.Invalid, "Изпитът още не е предаден."));

        var result = await Controller().ExamResult(token);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Take", redirect.ActionName);
    }

    [Fact]
    public async Task ExamResult_Unknown_ReturnsNotFound()
    {
        _service.Setup(s => s.GetExamResultAsync(It.IsAny<Guid>()))
            .ReturnsAsync(ServiceResult<QuizResult>.Failure(ServiceError.NotFound, "Опитът не е намерен."));

        Assert.IsType<NotFoundResult>(await Controller().ExamResult(Guid.NewGuid()));
    }
}
