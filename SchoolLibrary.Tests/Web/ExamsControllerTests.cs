using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Exams;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

public class ExamsControllerTests
{
    private readonly Mock<ISchoolClassService> _classes = new();
    private readonly Mock<IExamAssignmentService> _exams = new();

    public ExamsControllerTests()
    {
        _classes.Setup(s => s.GetForOwnerAsync(It.IsAny<string?>())).ReturnsAsync(new List<SchoolClassListItem>());
        _exams.Setup(s => s.GetAssignableQuizzesAsync(It.IsAny<string?>())).ReturnsAsync(new List<Quiz>());
    }

    private ExamsController Controller(string userId = "t1", params string[] roles) =>
        new ExamsController(_classes.Object, _exams.Object).WithUser(userId, roles.Length == 0 ? new[] { Roles.Teacher } : roles);

    private static ServiceResult<T> Fail<T>(ServiceError error) => ServiceResult<T>.Failure(error, "грешка");

    private static ExamAssignment Exam(int id = 3) => new()
    {
        Id = id,
        Code = "ABC123",
        Quiz = new Quiz { Id = 1, Title = "Дроби" },
        SchoolClass = new SchoolClass { Id = 2, Name = "5А", OwnerId = "t1" },
        TimeLimitMinutes = 20,
        MaxLeaves = 1
    };

    // ---------- Index ----------

    [Fact]
    public async Task Index_ForATeacher_ShowsTheirClassesAndExams()
    {
        _exams.Setup(s => s.GetForOwnerAsync("t1", 2)).ReturnsAsync(new List<ExamAssignmentListItem> { new(Exam(), 4, 1) });

        var result = await Controller().Index(classId: 2);

        var model = Assert.IsType<ExamsIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(2, model.SelectedClassId);
        Assert.Single(model.Exams);
        _classes.Verify(s => s.GetForOwnerAsync("t1"), Times.Once);
    }

    [Fact]
    public async Task Index_ForAnAdministrator_ShowsEveryone()
    {
        _exams.Setup(s => s.GetForOwnerAsync(null, null)).ReturnsAsync(new List<ExamAssignmentListItem>());

        await Controller("a1", Roles.Administrator).Index(null);

        _classes.Verify(s => s.GetForOwnerAsync(null), Times.Once);
        _exams.Verify(s => s.GetForOwnerAsync(null, null), Times.Once);
    }

    // ---------- Classes ----------

    [Fact]
    public void CreateClass_Get_ShowsAnEmptyForm()
    {
        var view = Assert.IsType<ViewResult>(Controller().CreateClass());

        Assert.Equal("ClassForm", view.ViewName);
        Assert.IsType<SchoolClassFormViewModel>(view.Model);
    }

    [Fact]
    public async Task CreateClass_Post_Success_RedirectsToTheList()
    {
        _classes.Setup(s => s.CreateAsync("5А", "t1")).ReturnsAsync(ServiceResult<SchoolClass>.Success(new SchoolClass { Name = "5А" }));

        var controller = Controller();
        var result = await controller.CreateClass(new SchoolClassFormViewModel { Name = "5А" });

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("5А", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task CreateClass_Post_Conflict_RedisplaysTheForm()
    {
        _classes.Setup(s => s.CreateAsync(It.IsAny<string>(), "t1")).ReturnsAsync(Fail<SchoolClass>(ServiceError.Conflict));

        var controller = Controller();
        var result = await controller.CreateClass(new SchoolClassFormViewModel { Name = "5А" });

        Assert.Equal("ClassForm", Assert.IsType<ViewResult>(result).ViewName);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task CreateClass_Post_InvalidModel_DoesNotCallTheService()
    {
        var controller = Controller();
        controller.ModelState.AddModelError("Name", "задължително");

        Assert.IsType<ViewResult>(await controller.CreateClass(new SchoolClassFormViewModel()));
        _classes.Verify(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EditClass_Get_ForTheOwner_ShowsTheName()
    {
        _classes.Setup(s => s.GetByIdAsync(2)).ReturnsAsync(new SchoolClass { Id = 2, Name = "5А", OwnerId = "t1" });

        var view = Assert.IsType<ViewResult>(await Controller().EditClass(2));

        Assert.Equal("5А", Assert.IsType<SchoolClassFormViewModel>(view.Model).Name);
        Assert.Equal(2, view.ViewData["ClassId"]);
    }

    [Fact]
    public async Task EditClass_Get_AnotherTeachersClass_IsForbidden_Missing_IsNotFound()
    {
        _classes.Setup(s => s.GetByIdAsync(2)).ReturnsAsync(new SchoolClass { Id = 2, Name = "5А", OwnerId = "t2" });

        Assert.IsType<ForbidResult>(await Controller().EditClass(2));
        Assert.IsType<NotFoundResult>(await Controller().EditClass(9));
    }

    [Theory]
    [InlineData(ServiceError.NotFound, typeof(NotFoundResult))]
    [InlineData(ServiceError.Forbidden, typeof(ForbidResult))]
    [InlineData(ServiceError.Conflict, typeof(ViewResult))]
    [InlineData(ServiceError.None, typeof(RedirectToActionResult))]
    public async Task EditClass_Post_MapsTheOutcome(ServiceError error, Type expected)
    {
        _classes.Setup(s => s.RenameAsync(2, "5Б", "t1", false)).ReturnsAsync(error == ServiceError.None
            ? ServiceResult<SchoolClass>.Success(new SchoolClass { Name = "5Б" })
            : Fail<SchoolClass>(error));

        var result = await Controller().EditClass(2, new SchoolClassFormViewModel { Name = "5Б" });

        Assert.IsType(expected, result);
    }

    [Theory]
    [InlineData(ServiceError.NotFound, typeof(NotFoundResult))]
    [InlineData(ServiceError.Forbidden, typeof(ForbidResult))]
    [InlineData(ServiceError.None, typeof(RedirectToActionResult))]
    public async Task DeleteClass_MapsTheOutcome(ServiceError error, Type expected)
    {
        _classes.Setup(s => s.DeleteAsync(2, "t1", false)).ReturnsAsync(error == ServiceError.None
            ? ServiceResult<SchoolClass>.Success(new SchoolClass { Name = "5А" })
            : Fail<SchoolClass>(error));

        Assert.IsType(expected, await Controller().DeleteClass(2));
    }

    // ---------- Assign ----------

    [Fact]
    public async Task Create_Get_PreselectsTheClassAndQuiz()
    {
        var model = Assert.IsType<ExamAssignFormViewModel>(Assert.IsType<ViewResult>(await Controller().Create(2, 1)).Model);

        Assert.Equal(2, model.SchoolClassId);
        Assert.Equal(1, model.QuizId);
        _exams.Verify(s => s.GetAssignableQuizzesAsync("t1"), Times.Once);
    }

    [Fact]
    public async Task Create_Post_Success_RedirectsToTheExamWithTheCode()
    {
        _exams.Setup(s => s.CreateAsync(new ExamAssignmentInput(1, 2, 20, null), "t1", false))
            .ReturnsAsync(ServiceResult<ExamAssignment>.Success(Exam(7)));

        var controller = Controller();
        var result = await controller.Create(new ExamAssignFormViewModel { QuizId = 1, SchoolClassId = 2, TimeLimitMinutes = 20 });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(7, redirect.RouteValues!["id"]);
        Assert.Contains("ABC123", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Create_Post_Invalid_RedisplaysWithTheChoices()
    {
        _exams.Setup(s => s.CreateAsync(It.IsAny<ExamAssignmentInput>(), "t1", false)).ReturnsAsync(Fail<ExamAssignment>(ServiceError.Invalid));

        var controller = Controller();
        var result = await controller.Create(new ExamAssignFormViewModel { QuizId = 1, SchoolClassId = 2 });

        Assert.IsType<ExamAssignFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Create_Post_Forbidden_IsForbid()
    {
        _exams.Setup(s => s.CreateAsync(It.IsAny<ExamAssignmentInput>(), "t1", false)).ReturnsAsync(Fail<ExamAssignment>(ServiceError.Forbidden));

        Assert.IsType<ForbidResult>(await Controller().Create(new ExamAssignFormViewModel { QuizId = 1, SchoolClassId = 2 }));
    }

    [Fact]
    public async Task Create_Post_InvalidModel_DoesNotCallTheService()
    {
        var controller = Controller();
        controller.ModelState.AddModelError("QuizId", "Изберете тест");

        Assert.IsType<ViewResult>(await controller.Create(new ExamAssignFormViewModel()));
        _exams.Verify(s => s.CreateAsync(It.IsAny<ExamAssignmentInput>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    // ---------- Details and changes ----------

    [Fact]
    public async Task Details_ShowsTheResultsAndTheCurrentSettings()
    {
        var results = new ExamResults(Exam(), new PagedResult<QuizAttempt>(new List<QuizAttempt>(), 0, 1, 50));
        _exams.Setup(s => s.GetResultsAsync(3, "t1", false, 1, It.IsAny<int>())).ReturnsAsync(ServiceResult<ExamResults>.Success(results));

        var model = Assert.IsType<ExamDetailsViewModel>(Assert.IsType<ViewResult>(await Controller().Details(3)).Model);

        Assert.Same(results, model.Results);
        Assert.Equal(20, model.Settings.TimeLimitMinutes);
        Assert.Equal(1, model.Settings.MaxLeaves);
        Assert.True(model.Settings.CodeHints);
    }

    [Fact]
    public async Task Create_And_UpdateSettings_PassTheCodeHintsChoice()
    {
        _exams.Setup(s => s.CreateAsync(It.IsAny<ExamAssignmentInput>(), "t1", false))
            .ReturnsAsync(ServiceResult<ExamAssignment>.Success(Exam(7)));
        _exams.Setup(s => s.UpdateSettingsAsync(3, It.IsAny<ExamSettingsInput>(), "t1", false))
            .ReturnsAsync(ServiceResult<ExamAssignment>.Success(Exam()));

        await Controller().Create(new ExamAssignFormViewModel { QuizId = 1, SchoolClassId = 2, CodeHints = false });
        await Controller().UpdateSettings(3, new ExamSettingsViewModel { TimeLimitMinutes = 30, CodeHints = false });

        _exams.Verify(s => s.CreateAsync(new ExamAssignmentInput(1, 2, null, null, false), "t1", false), Times.Once);
        _exams.Verify(s => s.UpdateSettingsAsync(3, new ExamSettingsInput(30, null, false), "t1", false), Times.Once);
    }

    [Theory]
    [InlineData(ServiceError.NotFound, typeof(NotFoundResult))]
    [InlineData(ServiceError.Forbidden, typeof(ForbidResult))]
    public async Task Details_Failures(ServiceError error, Type expected)
    {
        _exams.Setup(s => s.GetResultsAsync(3, "t1", false, 1, It.IsAny<int>())).ReturnsAsync(Fail<ExamResults>(error));

        Assert.IsType(expected, await Controller().Details(3));
    }

    [Fact]
    public async Task SetResults_Release_SavesAndRedirectsBack()
    {
        _exams.Setup(s => s.SetResultsReleasedAsync(3, true, "t1", false)).ReturnsAsync(ServiceResult<ExamAssignment>.Success(Exam()));

        var controller = Controller();
        var result = await controller.SetResults(3, release: true);

        Assert.Equal("Details", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("активирани", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task SetOpen_Close_SavesAndRedirectsBack()
    {
        _exams.Setup(s => s.SetOpenAsync(3, false, "t1", false)).ReturnsAsync(ServiceResult<ExamAssignment>.Success(Exam()));

        var controller = Controller();
        await controller.SetOpen(3, open: false);

        Assert.Contains("затворен", controller.TempDataText("SuccessMessage"));
    }

    [Theory]
    [InlineData(ServiceError.NotFound, typeof(NotFoundResult))]
    [InlineData(ServiceError.Forbidden, typeof(ForbidResult))]
    public async Task SetOpen_Failures(ServiceError error, Type expected)
    {
        _exams.Setup(s => s.SetOpenAsync(3, true, "t1", false)).ReturnsAsync(Fail<ExamAssignment>(error));

        Assert.IsType(expected, await Controller().SetOpen(3, true));
    }

    [Fact]
    public async Task UpdateSettings_Success_And_ServiceError_ShowTheMessage()
    {
        _exams.Setup(s => s.UpdateSettingsAsync(3, new ExamSettingsInput(30, null), "t1", false))
            .ReturnsAsync(ServiceResult<ExamAssignment>.Success(Exam()));
        _exams.Setup(s => s.UpdateSettingsAsync(3, new ExamSettingsInput(0, null), "t1", false))
            .ReturnsAsync(Fail<ExamAssignment>(ServiceError.Invalid));

        var ok = Controller();
        await ok.UpdateSettings(3, new ExamSettingsViewModel { TimeLimitMinutes = 30 });
        var bad = Controller();
        await bad.UpdateSettings(3, new ExamSettingsViewModel { TimeLimitMinutes = 0 });

        Assert.NotNull(ok.TempDataText("SuccessMessage"));
        Assert.Equal("грешка", bad.TempDataText("ErrorMessage"));
    }

    [Fact]
    public async Task UpdateSettings_InvalidModel_ShowsTheErrorsWithoutSaving()
    {
        var controller = Controller();
        controller.ModelState.AddModelError("TimeLimitMinutes", "Времето е между 1 и 600 минути");

        var result = await controller.UpdateSettings(3, new ExamSettingsViewModel());

        Assert.Equal("Details", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("600", controller.TempDataText("ErrorMessage"));
        _exams.Verify(s => s.UpdateSettingsAsync(It.IsAny<int>(), It.IsAny<ExamSettingsInput>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Theory]
    [InlineData(ServiceError.NotFound, typeof(NotFoundResult))]
    [InlineData(ServiceError.Forbidden, typeof(ForbidResult))]
    [InlineData(ServiceError.None, typeof(RedirectToActionResult))]
    public async Task Delete_MapsTheOutcome(ServiceError error, Type expected)
    {
        _exams.Setup(s => s.DeleteAsync(3, "t1", false)).ReturnsAsync(error == ServiceError.None
            ? ServiceResult<ExamAssignment>.Success(Exam())
            : Fail<ExamAssignment>(error));

        Assert.IsType(expected, await Controller().Delete(3));
    }

    // ---------- Deleting an attempt ----------

    [Fact]
    public async Task DeleteAttempt_Success_RedirectsBackToTheExamWithAMessage()
    {
        _exams.Setup(s => s.DeleteAttemptAsync(5, "t1", false)).ReturnsAsync(ServiceResult<QuizAttempt>.Success(
            new QuizAttempt { Id = 5, ExamAssignmentId = 3, StudentName = "Иван", StudentNumber = "7" }));

        var controller = Controller();
        var result = await controller.DeleteAttempt(5);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(3, redirect.RouteValues!["id"]);
        Assert.Contains("№ 7", controller.TempDataText("SuccessMessage"));
    }

    [Theory]
    [InlineData(ServiceError.NotFound, typeof(NotFoundResult))]
    [InlineData(ServiceError.Forbidden, typeof(ForbidResult))]
    public async Task DeleteAttempt_Failures(ServiceError error, Type expected)
    {
        _exams.Setup(s => s.DeleteAttemptAsync(5, "t1", false)).ReturnsAsync(Fail<QuizAttempt>(error));

        Assert.IsType(expected, await Controller().DeleteAttempt(5));
    }

    // ---------- One attempt ----------

    [Fact]
    public async Task Attempt_ShowsTheReview()
    {
        var review = new ExamAttemptReview(new QuizAttempt { Id = 5 }, new QuizResult(new Quiz(), 1, 2, new List<QuestionResult>()));
        _exams.Setup(s => s.GetAttemptReviewAsync(5, "t1", false)).ReturnsAsync(ServiceResult<ExamAttemptReview>.Success(review));

        Assert.Same(review, Assert.IsType<ViewResult>(await Controller().Attempt(5)).Model);
    }

    [Theory]
    [InlineData(ServiceError.NotFound, typeof(NotFoundResult))]
    [InlineData(ServiceError.Forbidden, typeof(ForbidResult))]
    [InlineData(ServiceError.Invalid, typeof(RedirectToActionResult))]
    public async Task Attempt_Failures(ServiceError error, Type expected)
    {
        _exams.Setup(s => s.GetAttemptReviewAsync(5, "t1", false)).ReturnsAsync(Fail<ExamAttemptReview>(error));

        Assert.IsType(expected, await Controller().Attempt(5));
    }
}
