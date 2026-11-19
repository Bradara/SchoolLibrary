using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

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

    // ---------- Details ----------

    [Fact]
    public async Task Details_ForOwnQuiz_ShowsTheManagePage()
    {
        _service.Setup(s => s.GetByIdAsync(5)).ReturnsAsync(new Quiz { Id = 5, OwnerId = "t1", Title = "Тест" });

        var result = await Controller("t1", Roles.Teacher).Details(5);

        var model = Assert.IsType<QuizManageViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(5, model.Quiz.Id);
    }

    [Fact]
    public async Task Details_Missing_ReturnsNotFound()
    {
        _service.Setup(s => s.GetByIdAsync(5)).ReturnsAsync((Quiz?)null);

        Assert.IsType<NotFoundResult>(await Controller("t1", Roles.Teacher).Details(5));
    }

    [Fact]
    public async Task Details_ForAnotherTeachersQuiz_ReturnsForbid()
    {
        _service.Setup(s => s.GetByIdAsync(5)).ReturnsAsync(new Quiz { Id = 5, OwnerId = "someone-else" });

        Assert.IsType<ForbidResult>(await Controller("t1", Roles.Teacher).Details(5));
    }

    // ---------- Create ----------

    [Fact]
    public void Create_Get_ShowsAnEmptyForm()
    {
        Assert.IsType<QuizFormViewModel>(Assert.IsType<ViewResult>(Controller("t1", Roles.Teacher).Create()).Model);
    }

    [Fact]
    public async Task Create_Post_Invalid_RedisplaysForm()
    {
        var controller = Controller("t1", Roles.Teacher);
        controller.ModelState.AddModelError("Title", "задължително");

        Assert.IsType<ViewResult>(await controller.Create(new QuizFormViewModel()));
        _service.Verify(s => s.CreateAsync(It.IsAny<QuizInput>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Create_Post_Success_RedirectsToTheOwnersNewQuiz()
    {
        _service.Setup(s => s.CreateAsync(It.Is<QuizInput>(i => i.Title == "Тест" && i.Mode == QuizMode.Practice), "t1"))
            .ReturnsAsync(ServiceResult<Quiz>.Success(new Quiz { Id = 7, Title = "Тест" }));

        var result = await Controller("t1", Roles.Teacher)
            .Create(new QuizFormViewModel { Title = "Тест", Mode = QuizMode.Practice });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(7, redirect.RouteValues!["id"]);
    }

    // ---------- Edit ----------

    [Fact]
    public async Task Edit_Get_MapsTheQuizIntoTheForm()
    {
        _service.Setup(s => s.GetByIdAsync(5))
            .ReturnsAsync(new Quiz { Id = 5, OwnerId = "t1", Title = "Старо", Mode = QuizMode.Exam, TimeLimitMinutes = 20 });

        var result = await Controller("t1", Roles.Teacher).Edit(5);

        var model = Assert.IsType<QuizFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Старо", model.Title);
        Assert.Equal(QuizMode.Exam, model.Mode);
        Assert.Equal(20, model.TimeLimitMinutes);
    }

    [Fact]
    public async Task Edit_Get_ForAnotherTeachersQuiz_ReturnsForbid()
    {
        _service.Setup(s => s.GetByIdAsync(5)).ReturnsAsync(new Quiz { Id = 5, OwnerId = "someone-else" });

        Assert.IsType<ForbidResult>(await Controller("t1", Roles.Teacher).Edit(5));
    }

    [Fact]
    public async Task Edit_Post_Success_RedirectsToDetails()
    {
        _service.Setup(s => s.UpdateAsync(5, It.IsAny<QuizInput>(), "t1", false))
            .ReturnsAsync(ServiceResult<Quiz>.Success(new Quiz { Id = 5 }));

        var result = await Controller("t1", Roles.Teacher).Edit(5, new QuizFormViewModel { Title = "Ново" });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
    }

    [Fact]
    public async Task Edit_Post_Conflict_RedisplaysFormWithError()
    {
        _service.Setup(s => s.UpdateAsync(5, It.IsAny<QuizInput>(), "t1", false))
            .ReturnsAsync(ServiceResult<Quiz>.Failure(ServiceError.Conflict, "вече има решения"));

        var controller = Controller("t1", Roles.Teacher);
        var result = await controller.Edit(5, new QuizFormViewModel { Title = "Ново" });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
    }

    // ---------- AddQuestion ----------

    [Fact]
    public async Task AddQuestion_DropsEmptyOptions_AndRemapsTheCorrectIndex()
    {
        QuestionInput? captured = null;
        _service.Setup(s => s.AddQuestionAsync(5, It.IsAny<QuestionInput>(), "t1", false))
            .Callback<int, QuestionInput, string, bool>((_, input, _, _) => captured = input)
            .ReturnsAsync(ServiceResult<Question>.Success(new Question()));

        // Raw options: [A, (empty), B, (empty)] with the correct one being B at raw index 2
        var vm = new QuestionFormViewModel
        {
            Text = "Въпрос?",
            Options = new List<string> { "A", "", "B", "" },
            CorrectIndex = 2
        };

        var result = await Controller("t1", Roles.Teacher).AddQuestion(5, vm);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.NotNull(captured);
        Assert.Equal(new[] { "A", "B" }, captured!.Options);
        Assert.Equal(1, captured.CorrectIndex);
        Assert.Equal("Въпрос?", captured.Text);
    }

    [Fact]
    public async Task AddQuestion_Failure_RedirectsToDetailsWithAnErrorMessage()
    {
        _service.Setup(s => s.AddQuestionAsync(5, It.IsAny<QuestionInput>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<Question>.Failure(ServiceError.Invalid, "Добавете поне 2 отговора."));

        var controller = Controller("t1", Roles.Teacher);
        var result = await controller.AddQuestion(5, new QuestionFormViewModel { Text = "В", Options = new List<string> { "само един" }, CorrectIndex = 0 });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("Добавете поне 2 отговора.", controller.TempDataText("ErrorMessage"));
    }

    [Fact]
    public async Task AddQuestion_WhenQuizMissing_ReturnsNotFound()
    {
        _service.Setup(s => s.AddQuestionAsync(It.IsAny<int>(), It.IsAny<QuestionInput>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<Question>.Failure(ServiceError.NotFound, "Тестът не е намерен."));

        var result = await Controller("t1", Roles.Teacher).AddQuestion(5, new QuestionFormViewModel { Text = "В", Options = new List<string> { "а", "б" }, CorrectIndex = 0 });

        Assert.IsType<NotFoundResult>(result);
    }

    // ---------- DeleteQuestion / SetPublished / Delete ----------

    [Fact]
    public async Task DeleteQuestion_Success_RedirectsToDetails()
    {
        _service.Setup(s => s.DeleteQuestionAsync(9, "t1", false))
            .ReturnsAsync(ServiceResult<Question>.Success(new Question()));

        var result = await Controller("t1", Roles.Teacher).DeleteQuestion(5, 9);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(5, redirect.RouteValues!["id"]);
    }

    [Fact]
    public async Task SetPublished_Success_RedirectsToDetails()
    {
        _service.Setup(s => s.SetPublishedAsync(5, true, "t1", false))
            .ReturnsAsync(ServiceResult<Quiz>.Success(new Quiz { Id = 5 }));

        var result = await Controller("t1", Roles.Teacher).SetPublished(5, true);

        Assert.Equal("Details", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    [Fact]
    public async Task SetPublished_Forbidden_ReturnsForbid()
    {
        _service.Setup(s => s.SetPublishedAsync(5, true, It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<Quiz>.Failure(ServiceError.Forbidden, "Нямате права."));

        Assert.IsType<ForbidResult>(await Controller("t1", Roles.Teacher).SetPublished(5, true));
    }

    [Fact]
    public async Task Delete_Success_RedirectsToIndex()
    {
        _service.Setup(s => s.DeleteAsync(5, "t1", false))
            .ReturnsAsync(ServiceResult<Quiz>.Success(new Quiz { Id = 5, Title = "Тест" }));

        var result = await Controller("t1", Roles.Teacher).Delete(5);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    [Fact]
    public async Task Delete_Missing_ReturnsNotFound()
    {
        _service.Setup(s => s.DeleteAsync(5, It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<Quiz>.Failure(ServiceError.NotFound, "Тестът не е намерен."));

        Assert.IsType<NotFoundResult>(await Controller("t1", Roles.Teacher).Delete(5));
    }
}
