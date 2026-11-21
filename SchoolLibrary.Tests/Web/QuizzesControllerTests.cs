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
    public async Task Solve_Get_ReturnsThePracticeSet()
    {
        var set = new PracticeSet(new Quiz { Id = 5, Title = "Дроби" }, new List<Question>());
        _service.Setup(s => s.GetPracticeSetAsync(5)).ReturnsAsync(ServiceResult<PracticeSet>.Success(set));

        var result = await Controller().Solve(5);

        Assert.Same(set, Assert.IsType<ViewResult>(result).Model);
    }

    [Fact]
    public async Task Solve_Get_Missing_ReturnsNotFound()
    {
        _service.Setup(s => s.GetPracticeSetAsync(5))
            .ReturnsAsync(ServiceResult<PracticeSet>.Failure(ServiceError.NotFound, "Упражнението не е намерено."));

        Assert.IsType<NotFoundResult>(await Controller().Solve(5));
    }

    [Fact]
    public async Task Solve_Post_GradesTheShownQuestionsAndShowsTheResult()
    {
        var graded = new QuizResult(new Quiz { Id = 5, Title = "Дроби" }, 1, 2, new List<QuestionResult>());
        _service.Setup(s => s.GradePracticeAsync(5, It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<IReadOnlyDictionary<int, SubmittedAnswer>>()))
            .ReturnsAsync(ServiceResult<QuizResult>.Success(graded));

        var result = await Controller().Solve(5, new List<int> { 1 }, new Dictionary<int, int?> { [1] = 2 });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Result", view.ViewName);
        Assert.Same(graded, view.Model);
    }

    [Fact]
    public async Task Solve_Post_WithNoAnswers_GradesWithEmptySets()
    {
        var graded = new QuizResult(new Quiz { Id = 5 }, 0, 2, new List<QuestionResult>());
        _service.Setup(s => s.GradePracticeAsync(5, It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<IReadOnlyDictionary<int, SubmittedAnswer>>()))
            .ReturnsAsync(ServiceResult<QuizResult>.Success(graded));

        var result = await Controller().Solve(5, null, null);

        Assert.IsType<ViewResult>(result);
        _service.Verify(s => s.GradePracticeAsync(5,
            It.Is<IReadOnlyCollection<int>>(ids => ids.Count == 0),
            It.Is<IReadOnlyDictionary<int, SubmittedAnswer>>(d => d.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task Solve_Post_Missing_ReturnsNotFound()
    {
        _service.Setup(s => s.GradePracticeAsync(5, It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<IReadOnlyDictionary<int, SubmittedAnswer>>()))
            .ReturnsAsync(ServiceResult<QuizResult>.Failure(ServiceError.NotFound, "Упражнението не е намерено."));

        Assert.IsType<NotFoundResult>(await Controller().Solve(5, new List<int>(), new Dictionary<int, int?>()));
    }

    // ---------- Exam: entry (step 1 — code, step 2 — name and number) ----------

    private static ExamEntry Entry(string code = "ABC123", string? className = "5А", bool open = true)
        => new(code, "Изпит по дроби", className, 30, open);

    [Fact]
    public void Exam_Get_ShowsAnEmptyCodeForm()
    {
        var model = Assert.IsType<ExamCodeViewModel>(Assert.IsType<ViewResult>(Controller().Exam()).Model);
        Assert.Equal(string.Empty, model.Code);
    }

    [Fact]
    public async Task Exam_Post_WithInvalidModel_RedisplaysTheCodeForm()
    {
        var controller = Controller();
        controller.ModelState.AddModelError("Code", "задължително");

        var result = await controller.Exam(new ExamCodeViewModel());

        Assert.IsType<ExamCodeViewModel>(Assert.IsType<ViewResult>(result).Model);
        _service.Verify(s => s.FindExamAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Exam_Post_UnknownCode_RedisplaysTheCodeFormWithTheMessage()
    {
        _service.Setup(s => s.FindExamAsync("ZZZ"))
            .ReturnsAsync(ServiceResult<ExamEntry>.Failure(ServiceError.NotFound, "Няма активен изпит с този код."));

        var controller = Controller();
        var result = await controller.Exam(new ExamCodeViewModel { Code = "ZZZ" });

        Assert.IsType<ExamCodeViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Exam_Post_KnownCode_AsksForTheNameAndNumber()
    {
        var entry = Entry();
        _service.Setup(s => s.FindExamAsync("abc123")).ReturnsAsync(ServiceResult<ExamEntry>.Success(entry));

        var result = await Controller().Exam(new ExamCodeViewModel { Code = "abc123" });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ExamStudent", view.ViewName);
        var model = Assert.IsType<ExamEntryViewModel>(view.Model);
        Assert.Equal("ABC123", model.Code); // the normalized code goes into the hidden field
        Assert.Same(entry, model.Exam);
        _service.Verify(s => s.StartExamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ExamStudent_Post_Success_RedirectsToTheAttempt()
    {
        var token = Guid.NewGuid();
        _service.Setup(s => s.FindExamAsync("ABC123")).ReturnsAsync(ServiceResult<ExamEntry>.Success(Entry()));
        _service.Setup(s => s.StartExamAsync("ABC123", "Иван", "5"))
            .ReturnsAsync(ServiceResult<QuizAttempt>.Success(new QuizAttempt { Token = token }));

        var result = await Controller().ExamStudent(new ExamEntryViewModel { Code = "ABC123", StudentName = "Иван", StudentNumber = "5" });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Take", redirect.ActionName);
        Assert.Equal(token, redirect.RouteValues!["token"]);
    }

    [Fact]
    public async Task ExamStudent_Post_WithInvalidModel_RedisplaysWithTheExamShown()
    {
        var entry = Entry();
        _service.Setup(s => s.FindExamAsync("ABC123")).ReturnsAsync(ServiceResult<ExamEntry>.Success(entry));
        var controller = Controller();
        controller.ModelState.AddModelError("StudentNumber", "задължително");

        var result = await controller.ExamStudent(new ExamEntryViewModel { Code = "ABC123", StudentName = "Иван" });

        var model = Assert.IsType<ExamEntryViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Same(entry, model.Exam);
        _service.Verify(s => s.StartExamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData(ServiceError.Invalid)]
    [InlineData(ServiceError.Conflict)]
    public async Task ExamStudent_Post_WhenStartFails_RedisplaysTheFormWithTheMessage(ServiceError error)
    {
        _service.Setup(s => s.FindExamAsync("ABC123")).ReturnsAsync(ServiceResult<ExamEntry>.Success(Entry()));
        _service.Setup(s => s.StartExamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<QuizAttempt>.Failure(error, "грешка"));

        var controller = Controller();
        var result = await controller.ExamStudent(new ExamEntryViewModel { Code = "ABC123", StudentName = "Иван", StudentNumber = "5" });

        var model = Assert.IsType<ExamEntryViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.NotNull(model.Exam);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task ExamStudent_Post_CodeNoLongerFound_GoesBackToTheCodeForm()
    {
        _service.Setup(s => s.FindExamAsync("GONE"))
            .ReturnsAsync(ServiceResult<ExamEntry>.Failure(ServiceError.NotFound, "Няма активен изпит с този код."));

        var result = await Controller().ExamStudent(new ExamEntryViewModel { Code = "GONE", StudentName = "Иван", StudentNumber = "5" });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Exam", view.ViewName);
        Assert.IsType<ExamCodeViewModel>(view.Model);
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
        _service.Setup(s => s.SubmitExamAsync(token, It.IsAny<IReadOnlyDictionary<int, SubmittedAnswer>>()))
            .ReturnsAsync(ServiceResult<QuizResult>.Success(new QuizResult(new Quiz(), 1, 2, new List<QuestionResult>())));

        var result = await Controller().Take(token, new Dictionary<int, int?> { [1] = 2 });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ExamResult", redirect.ActionName);
        Assert.Equal(token, redirect.RouteValues!["token"]);
    }

    [Fact]
    public async Task Take_Post_Unknown_ReturnsNotFound()
    {
        _service.Setup(s => s.SubmitExamAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyDictionary<int, SubmittedAnswer>>()))
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
    public async Task Take_Get_FlaggedButNotSubmitted_SubmitsWithNoAnswers()
    {
        var attempt = AttemptInProgress();
        attempt.Flagged = true;
        _service.Setup(s => s.GetExamAttemptAsync(attempt.Token)).ReturnsAsync(attempt);
        _service.Setup(s => s.SubmitExamAsync(attempt.Token, It.IsAny<IReadOnlyDictionary<int, SubmittedAnswer>>()))
            .ReturnsAsync(ServiceResult<QuizResult>.Success(new QuizResult(new Quiz(), 0, 2, new List<QuestionResult>())));

        var result = await Controller().Take(attempt.Token);

        Assert.Equal("ExamResult", Assert.IsType<RedirectToActionResult>(result).ActionName);
        _service.Verify(s => s.SubmitExamAsync(attempt.Token, It.Is<IReadOnlyDictionary<int, SubmittedAnswer>>(d => d.Count == 0)), Times.Once);
    }

    private static QuizAttempt ForcedAttempt(Guid token) => new()
    {
        Token = token,
        StudentName = "Иван",
        Flagged = true,
        SubmittedOn = DateTime.UtcNow,
        Quiz = new Quiz { Title = "Изпит" }
    };

    [Fact]
    public async Task ExamResult_ForcedSubmission_ShowsTheNoticeWithTheGradeButNoAnswers()
    {
        var token = Guid.NewGuid();
        var graded = new QuizResult(new Quiz { Title = "Изпит" }, 1, 2, new List<QuestionResult>());
        _service.Setup(s => s.GetExamResultAsync(token)).ReturnsAsync(ServiceResult<QuizResult>.Success(graded));
        _service.Setup(s => s.GetExamAttemptAsync(token)).ReturnsAsync(ForcedAttempt(token));

        var view = Assert.IsType<ViewResult>(await Controller().ExamResult(token));

        Assert.Equal("ExamForced", view.ViewName);
        var model = Assert.IsType<ExamForcedViewModel>(view.Model);
        Assert.Same(graded, model.Result);
        Assert.Equal("Иван", model.StudentName);
    }

    [Fact]
    public async Task ExamResult_ForcedSubmission_NotReleased_ShowsTheNoticeWithoutAResult()
    {
        var token = Guid.NewGuid();
        _service.Setup(s => s.GetExamResultAsync(token))
            .ReturnsAsync(ServiceResult<QuizResult>.Failure(ServiceError.Forbidden, "Резултатът още не е активиран от учителя."));
        _service.Setup(s => s.GetExamAttemptAsync(token)).ReturnsAsync(ForcedAttempt(token));

        var view = Assert.IsType<ViewResult>(await Controller().ExamResult(token));

        Assert.Equal("ExamForced", view.ViewName);
        Assert.Null(Assert.IsType<ExamForcedViewModel>(view.Model).Result);
    }

    [Fact]
    public async Task ExamResult_NotReleasedYet_ShowsThePendingPage()
    {
        var token = Guid.NewGuid();
        _service.Setup(s => s.GetExamResultAsync(token))
            .ReturnsAsync(ServiceResult<QuizResult>.Failure(ServiceError.Forbidden, "Резултатът още не е активиран от учителя."));
        _service.Setup(s => s.GetExamAttemptAsync(token)).ReturnsAsync(new QuizAttempt
        {
            Token = token,
            StudentName = "Иван",
            SubmittedOn = DateTime.UtcNow,
            Quiz = new Quiz { Title = "Изпит" },
            ExamAssignment = new ExamAssignment { SchoolClass = new SchoolClass { Name = "5А" } }
        });

        var result = await Controller().ExamResult(token);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("ExamPending", view.ViewName);
        var model = Assert.IsType<ExamPendingViewModel>(view.Model);
        Assert.Equal("Изпит", model.Title);
        Assert.Equal("5А", model.ClassName);
        Assert.Equal("Иван", model.StudentName);
    }

    [Fact]
    public async Task Take_Get_ClassExam_UsesTheExamTimeLimit()
    {
        var attempt = AttemptInProgress(timeLimit: null);
        attempt.ExamAssignment = new ExamAssignment { TimeLimitMinutes = 10 };
        _service.Setup(s => s.GetExamAttemptAsync(attempt.Token)).ReturnsAsync(attempt);

        var result = await Controller().Take(attempt.Token);

        var model = Assert.IsType<ExamTakeViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.InRange(model.RemainingSeconds!.Value, 1, 10 * 60);
    }

    [Fact]
    public async Task ExamResult_Unknown_ReturnsNotFound()
    {
        _service.Setup(s => s.GetExamResultAsync(It.IsAny<Guid>()))
            .ReturnsAsync(ServiceResult<QuizResult>.Failure(ServiceError.NotFound, "Опитът не е намерен."));

        Assert.IsType<NotFoundResult>(await Controller().ExamResult(Guid.NewGuid()));
    }

    // ---------- Proctoring ping ----------

    [Fact]
    public async Task RecordLeave_ReturnsTheStatusAsJson()
    {
        var token = Guid.NewGuid();
        _service.Setup(s => s.RecordLeaveAsync(token))
            .ReturnsAsync(ServiceResult<ProctorStatus>.Success(new ProctorStatus(2, true, true)));

        var result = await Controller().RecordLeave(token);

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;
        Assert.Equal(2, value.GetType().GetProperty("leaveCount")!.GetValue(value));
        Assert.Equal(true, value.GetType().GetProperty("shouldSubmit")!.GetValue(value));
    }

    [Fact]
    public async Task RecordLeave_Unknown_ReturnsNotFound()
    {
        _service.Setup(s => s.RecordLeaveAsync(It.IsAny<Guid>()))
            .ReturnsAsync(ServiceResult<ProctorStatus>.Failure(ServiceError.NotFound, "Опитът не е намерен."));

        Assert.IsType<NotFoundResult>(await Controller().RecordLeave(Guid.NewGuid()));
    }
}
