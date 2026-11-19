using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

public class SectionsControllerTests
{
    private readonly Mock<ISectionService> _sections = new();
    private readonly Mock<IQuestionBankService> _questions = new();

    private SectionsController Controller(string userId, params string[] roles) =>
        new SectionsController(_sections.Object, _questions.Object).WithUser(userId, roles);

    private static PagedResult<SectionListItem> EmptyPage() =>
        new(new List<SectionListItem>(), totalCount: 0, page: 1, pageSize: 10);

    // ---------- Index ----------

    [Fact]
    public async Task Index_ForATeacher_ListsOnlyTheirSections()
    {
        _sections.Setup(s => s.GetManagedAsync("t1", It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(EmptyPage());

        Assert.IsType<ViewResult>(await Controller("t1", Roles.Teacher).Index());
        _sections.Verify(s => s.GetManagedAsync("t1", 1, It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task Index_ForAnAdministrator_ListsEverySection()
    {
        _sections.Setup(s => s.GetManagedAsync(null, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(EmptyPage());

        Assert.IsType<ViewResult>(await Controller("admin", Roles.Administrator).Index());
        _sections.Verify(s => s.GetManagedAsync(null, 1, It.IsAny<int>()), Times.Once);
    }

    // ---------- Details ----------

    [Fact]
    public async Task Details_ForOwnSection_ShowsItWithQuestions()
    {
        _sections.Setup(s => s.GetByIdAsync(5)).ReturnsAsync(new Section { Id = 5, OwnerId = "t1", Name = "Дроби" });
        _questions.Setup(q => q.GetBySectionAsync(5)).ReturnsAsync(new List<Question>());

        var result = await Controller("t1", Roles.Teacher).Details(5);

        var model = Assert.IsType<SectionManageViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(5, model.Section.Id);
    }

    [Fact]
    public async Task Details_Missing_ReturnsNotFound()
    {
        _sections.Setup(s => s.GetByIdAsync(5)).ReturnsAsync((Section?)null);

        Assert.IsType<NotFoundResult>(await Controller("t1", Roles.Teacher).Details(5));
    }

    [Fact]
    public async Task Details_ForAnotherTeachersSection_ReturnsForbid()
    {
        _sections.Setup(s => s.GetByIdAsync(5)).ReturnsAsync(new Section { Id = 5, OwnerId = "someone-else" });

        Assert.IsType<ForbidResult>(await Controller("t1", Roles.Teacher).Details(5));
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_Post_Success_RedirectsToTheNewSection()
    {
        _sections.Setup(s => s.CreateAsync(It.Is<SectionInput>(i => i.Name == "Дроби"), "t1"))
            .ReturnsAsync(ServiceResult<Section>.Success(new Section { Id = 7, Name = "Дроби" }));

        var result = await Controller("t1", Roles.Teacher).Create(new SectionFormViewModel { Name = "Дроби" });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(7, redirect.RouteValues!["id"]);
    }

    [Fact]
    public async Task Create_Post_Invalid_RedisplaysForm()
    {
        var controller = Controller("t1", Roles.Teacher);
        controller.ModelState.AddModelError("Name", "задължително");

        Assert.IsType<ViewResult>(await controller.Create(new SectionFormViewModel()));
        _sections.Verify(s => s.CreateAsync(It.IsAny<SectionInput>(), It.IsAny<string>()), Times.Never);
    }

    // ---------- Edit ----------

    [Fact]
    public async Task Edit_Get_MapsTheSectionIntoTheForm()
    {
        _sections.Setup(s => s.GetByIdAsync(5)).ReturnsAsync(new Section { Id = 5, OwnerId = "t1", Name = "Старо", Description = "описание" });

        var result = await Controller("t1", Roles.Teacher).Edit(5);

        var model = Assert.IsType<SectionFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Старо", model.Name);
        Assert.Equal("описание", model.Description);
    }

    [Fact]
    public async Task Edit_Post_Success_RedirectsToDetails()
    {
        _sections.Setup(s => s.UpdateAsync(5, It.IsAny<SectionInput>(), "t1", false))
            .ReturnsAsync(ServiceResult<Section>.Success(new Section { Id = 5 }));

        var result = await Controller("t1", Roles.Teacher).Edit(5, new SectionFormViewModel { Name = "Ново" });

        Assert.Equal("Details", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_Success_RedirectsToIndex()
    {
        _sections.Setup(s => s.DeleteAsync(5, "t1", false))
            .ReturnsAsync(ServiceResult<Section>.Success(new Section { Id = 5, Name = "Дроби" }));

        var result = await Controller("t1", Roles.Teacher).Delete(5);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    [Fact]
    public async Task Delete_Conflict_RedirectsToDetailsWithAnError()
    {
        _sections.Setup(s => s.DeleteAsync(5, It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<Section>.Failure(ServiceError.Conflict, "съдържа въпроси"));

        var controller = Controller("t1", Roles.Teacher);
        var result = await controller.Delete(5);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("съдържа въпроси", controller.TempDataText("ErrorMessage"));
    }

    // ---------- AddQuestion ----------

    [Fact]
    public async Task AddQuestion_Success_RedirectsToDetails()
    {
        _questions.Setup(q => q.CreateAsync(5, It.IsAny<QuestionInput>(), "t1", false))
            .ReturnsAsync(ServiceResult<Question>.Success(new Question()));

        var vm = new QuestionFormViewModel { Text = "В?", Options = new List<string> { "a", "b" }, CorrectIndex = 0 };
        var result = await Controller("t1", Roles.Teacher).AddQuestion(5, vm);

        Assert.Equal("Details", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    [Fact]
    public async Task AddQuestion_Invalid_RedisplaysTheSectionPageWithTheQuestionKept()
    {
        _questions.Setup(q => q.CreateAsync(5, It.IsAny<QuestionInput>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<Question>.Failure(ServiceError.Invalid, "Посочете кой е верният отговор."));
        _sections.Setup(s => s.GetByIdAsync(5)).ReturnsAsync(new Section { Id = 5, OwnerId = "t1" });
        _questions.Setup(q => q.GetBySectionAsync(5)).ReturnsAsync(new List<Question>());

        var controller = Controller("t1", Roles.Teacher);
        var vm = new QuestionFormViewModel { Text = "Моят въпрос", Options = new List<string> { "a", "b" }, CorrectIndex = -1 };
        var result = await controller.AddQuestion(5, vm);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Details", view.ViewName);
        var model = Assert.IsType<SectionManageViewModel>(view.Model);
        Assert.Equal("Моят въпрос", model.NewQuestion.Text);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task AddQuestion_MissingSection_ReturnsNotFound()
    {
        _questions.Setup(q => q.CreateAsync(It.IsAny<int>(), It.IsAny<QuestionInput>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<Question>.Failure(ServiceError.NotFound, "Разделът не е намерен."));

        var vm = new QuestionFormViewModel { Text = "В?", Options = new List<string> { "a", "b" }, CorrectIndex = 0 };
        Assert.IsType<NotFoundResult>(await Controller("t1", Roles.Teacher).AddQuestion(5, vm));
    }

    // ---------- EditQuestion ----------

    [Fact]
    public async Task EditQuestion_Get_MapsTheBankQuestionIntoTheForm()
    {
        _questions.Setup(q => q.GetQuestionAsync(9)).ReturnsAsync(new Question
        {
            Id = 9,
            SectionId = 5,
            Text = "Въпрос?",
            Points = 3,
            Section = new Section { Id = 5, OwnerId = "t1" },
            Options = new List<AnswerOption>
            {
                new() { Text = "A", Order = 1, IsCorrect = false },
                new() { Text = "B", Order = 2, IsCorrect = true }
            }
        });

        var result = await Controller("t1", Roles.Teacher).EditQuestion(5, 9);

        var model = Assert.IsType<QuestionFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Въпрос?", model.Text);
        Assert.Equal(new[] { "A", "B" }, model.Options);
        Assert.Equal(1, model.CorrectIndex);
        Assert.Equal(3, model.Points);
    }

    [Fact]
    public async Task EditQuestion_Get_ForAnotherTeachersSection_ReturnsForbid()
    {
        _questions.Setup(q => q.GetQuestionAsync(9)).ReturnsAsync(new Question
        {
            Id = 9,
            SectionId = 5,
            Section = new Section { Id = 5, OwnerId = "someone-else" }
        });

        Assert.IsType<ForbidResult>(await Controller("t1", Roles.Teacher).EditQuestion(5, 9));
    }

    [Fact]
    public async Task DeleteQuestion_Success_RedirectsToDetails()
    {
        _questions.Setup(q => q.DeleteAsync(9, "t1", false))
            .ReturnsAsync(ServiceResult<Question>.Success(new Question()));

        var result = await Controller("t1", Roles.Teacher).DeleteQuestion(5, 9);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(5, redirect.RouteValues!["id"]);
    }
}
