using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Areas.Admin.Controllers;
using SchoolLibrary.Areas.Admin.Models;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Statistics;
using SchoolLibrary.Services.Users;

namespace SchoolLibrary.Tests.Web;

public class AdminHomeControllerTests
{
    [Fact]
    public async Task Index_ShowsTheTotalsOfTheDashboard()
    {
        var statistics = new Mock<IStatisticsService>();
        var totals = new DashboardStatistics(10, 44, 31, 2, 3);
        statistics.Setup(s => s.GetDashboardAsync()).ReturnsAsync(totals);
        var controller = new SchoolLibrary.Areas.Admin.Controllers.HomeController(statistics.Object).WithUser("admin", Roles.Administrator);

        var result = await controller.Index();

        Assert.Equal(totals, Assert.IsType<ViewResult>(result).Model);
    }
}

public class AdminCategoriesControllerTests
{
    private readonly Mock<ICategoryService> _service = new();

    private CategoriesController Controller() => new CategoriesController(_service.Object).WithUser("admin", Roles.Administrator);

    private static ServiceResult<Category> Ok(string name = "Музика") => ServiceResult<Category>.Success(new Category { Id = 3, Name = name });

    private static ServiceResult<Category> Fail(ServiceError error, string message = "грешка") => ServiceResult<Category>.Failure(error, message);

    [Fact]
    public async Task Index_ListsTheSubjects()
    {
        var categories = new List<Category> { new() { Name = "А" }, new() { Name = "Б" } };
        _service.Setup(s => s.GetAllAsync()).ReturnsAsync(categories);

        var result = await Controller().Index();

        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<Category>>(Assert.IsType<ViewResult>(result).Model).Count());
    }

    [Theory]
    [InlineData(null, typeof(NotFoundResult))]
    [InlineData(99, typeof(NotFoundResult))]
    [InlineData(3, typeof(ViewResult))]
    public async Task Details_NeedsAnExistingSubject(int? id, Type expected)
    {
        _service.Setup(s => s.GetWithResourcesAsync(3)).ReturnsAsync(new Category { Id = 3, Name = "Музика" });

        Assert.IsType(expected, await Controller().Details(id));
    }

    [Fact]
    public void Create_Get_ShowsAnEmptyForm()
    {
        Assert.IsType<ViewResult>(Controller().Create());
    }

    [Fact]
    public async Task Create_Post_Success_SaysSo_AndGoesToTheList()
    {
        _service.Setup(s => s.CreateAsync("Музика")).ReturnsAsync(Ok());
        var controller = Controller();

        var result = await controller.Create(new Category { Name = "Музика" });

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("Музика", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ShowsTheFormAgainWithoutSaving()
    {
        var controller = Controller();
        controller.ModelState.AddModelError("Name", "Името е задължително");

        var result = await controller.Create(new Category());

        Assert.IsType<ViewResult>(result);
        _service.Verify(s => s.CreateAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Create_Post_ANameThatIsTaken_ShowsTheMessageOnTheNameField()
    {
        _service.Setup(s => s.CreateAsync("Музика")).ReturnsAsync(Fail(ServiceError.Conflict, "Предмет с име Музика вече съществува."));
        var controller = Controller();

        var result = await controller.Create(new Category { Name = "Музика" });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState["Name"]!.Errors, e => e.ErrorMessage.Contains("вече съществува"));
    }

    [Theory]
    [InlineData(null, typeof(NotFoundResult))]
    [InlineData(99, typeof(NotFoundResult))]
    [InlineData(3, typeof(ViewResult))]
    public async Task Edit_Get_NeedsAnExistingSubject(int? id, Type expected)
    {
        _service.Setup(s => s.GetByIdAsync(3)).ReturnsAsync(new Category { Id = 3, Name = "Музика" });

        Assert.IsType(expected, await Controller().Edit(id));
    }

    [Fact]
    public async Task Edit_Post_AnotherId_ReturnsNotFound()
    {
        Assert.IsType<NotFoundResult>(await Controller().Edit(3, new Category { Id = 4, Name = "Музика" }));
    }

    [Fact]
    public async Task Edit_Post_Success_SaysSo_AndGoesToTheList()
    {
        _service.Setup(s => s.UpdateAsync(3, "Музика")).ReturnsAsync(Ok());
        var controller = Controller();

        var result = await controller.Edit(3, new Category { Id = 3, Name = "Музика" });

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("променен", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Edit_Post_InvalidModel_ShowsTheFormAgain()
    {
        var controller = Controller();
        controller.ModelState.AddModelError("Name", "Името е задължително");

        Assert.IsType<ViewResult>(await controller.Edit(3, new Category { Id = 3 }));
        _service.Verify(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Edit_Post_UnknownSubject_ReturnsNotFound()
    {
        _service.Setup(s => s.UpdateAsync(3, "Музика")).ReturnsAsync(Fail(ServiceError.NotFound));

        Assert.IsType<NotFoundResult>(await Controller().Edit(3, new Category { Id = 3, Name = "Музика" }));
    }

    [Fact]
    public async Task Edit_Post_ANameThatIsTaken_ShowsTheMessageOnTheNameField()
    {
        _service.Setup(s => s.UpdateAsync(3, "Музика")).ReturnsAsync(Fail(ServiceError.Conflict, "вече съществува"));
        var controller = Controller();

        var result = await controller.Edit(3, new Category { Id = 3, Name = "Музика" });

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState["Name"]!.Errors.Count > 0);
    }

    [Theory]
    [InlineData(null, typeof(NotFoundResult))]
    [InlineData(99, typeof(NotFoundResult))]
    [InlineData(3, typeof(ViewResult))]
    public async Task Delete_Get_NeedsAnExistingSubject(int? id, Type expected)
    {
        _service.Setup(s => s.GetByIdAsync(3)).ReturnsAsync(new Category { Id = 3, Name = "Музика" });

        Assert.IsType(expected, await Controller().Delete(id));
    }

    [Fact]
    public async Task DeleteConfirmed_Success_SaysSo()
    {
        _service.Setup(s => s.DeleteAsync(3)).ReturnsAsync(Ok());
        var controller = Controller();

        var result = await controller.DeleteConfirmed(3);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("изтрит", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task DeleteConfirmed_ASubjectThatStillHasResources_ShowsWhy_AndKeepsIt()
    {
        _service.Setup(s => s.DeleteAsync(3)).ReturnsAsync(Fail(ServiceError.Conflict, "Предметът не може да бъде изтрит — има 4 свързани ресурса."));
        var controller = Controller();

        var result = await controller.DeleteConfirmed(3);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("не може да бъде изтрит", controller.TempDataText("ErrorMessage"));
        Assert.Null(controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownSubject_ReturnsNotFound()
    {
        _service.Setup(s => s.DeleteAsync(99)).ReturnsAsync(Fail(ServiceError.NotFound));

        Assert.IsType<NotFoundResult>(await Controller().DeleteConfirmed(99));
    }
}

public class AdminUsersControllerTests
{
    private readonly Mock<IUserService> _service = new();

    private UsersController Controller() => new UsersController(_service.Object).WithUser("admin", Roles.Administrator);

    private static UserListItem Item(string name = "Иван Петров") =>
        new("u1", name, "ivan@example.com", DateTime.UtcNow, false, new List<string>());

    [Fact]
    public async Task Index_PassesTheSearchAndShowsThePaging()
    {
        UserFilter? filter = null;
        _service.Setup(s => s.SearchAsync(It.IsAny<UserFilter>()))
            .Callback<UserFilter>(f => filter = f)
            .ReturnsAsync(new PagedResult<UserListItem>(new List<UserListItem> { Item() }, 25, 2, 10));

        var result = await Controller().Index("иван", pending: true, page: 2);

        Assert.Equal("иван", filter!.SearchText);
        Assert.True(filter.OnlyPendingTeachers);
        Assert.Equal(2, filter.Page);
        var model = Assert.IsType<UserIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("иван", model.Search);
        Assert.True(model.Pending);
        Assert.Single(model.Users);
        Assert.Equal(3, model.TotalPages);
        Assert.Equal("admin", model.CurrentUserId);
    }

    [Fact]
    public async Task ApproveTeacher_Success_NamesTheTeacher()
    {
        _service.Setup(s => s.ApproveTeacherAsync("u1")).ReturnsAsync(ServiceResult<UserListItem>.Success(Item()));
        var controller = Controller();

        var result = await controller.ApproveTeacher("u1");

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("Иван Петров вече е учител", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task RejectTeacher_Success_NamesTheTeacher()
    {
        _service.Setup(s => s.RejectTeacherAsync("u1")).ReturnsAsync(ServiceResult<UserListItem>.Success(Item()));
        var controller = Controller();

        await controller.RejectTeacher("u1");

        Assert.Contains("Иван Петров", controller.TempDataText("SuccessMessage"));
        Assert.Contains("отхвърлена", controller.TempDataText("SuccessMessage"));
    }

    [Theory]
    [InlineData(true, "добавена е роля „Учител“")]
    [InlineData(false, "премахната е роля „Учител“")]
    public async Task SetRole_SaysWhichRoleWasAddedOrRemoved_AsTheSignedInAdministrator(bool enabled, string messagePart)
    {
        _service.Setup(s => s.SetRoleAsync("u1", Roles.Teacher, enabled, "admin")).ReturnsAsync(ServiceResult<UserListItem>.Success(Item()));
        var controller = Controller();

        await controller.SetRole("u1", Roles.Teacher, enabled);

        Assert.Contains(messagePart, controller.TempDataText("SuccessMessage"));
        _service.Verify(s => s.SetRoleAsync("u1", Roles.Teacher, enabled, "admin"), Times.Once);
    }

    [Fact]
    public async Task AnAction_RefusedByTheService_ShowsTheError()
    {
        _service.Setup(s => s.SetRoleAsync("admin", Roles.Administrator, false, "admin"))
            .ReturnsAsync(ServiceResult<UserListItem>.Failure(ServiceError.Forbidden, "Не можете да премахнете собствената си администраторска роля."));
        var controller = Controller();

        var result = await controller.SetRole("admin", Roles.Administrator, false);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("собствената си", controller.TempDataText("ErrorMessage"));
        Assert.Null(controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task AnAction_OnAnUnknownUser_ReturnsNotFound()
    {
        var notFound = ServiceResult<UserListItem>.Failure(ServiceError.NotFound, "Потребителят не е намерен.");
        _service.Setup(s => s.ApproveTeacherAsync("x")).ReturnsAsync(notFound);
        _service.Setup(s => s.RejectTeacherAsync("x")).ReturnsAsync(notFound);
        _service.Setup(s => s.SetRoleAsync("x", Roles.Teacher, true, "admin")).ReturnsAsync(notFound);

        Assert.IsType<NotFoundResult>(await Controller().ApproveTeacher("x"));
        Assert.IsType<NotFoundResult>(await Controller().RejectTeacher("x"));
        Assert.IsType<NotFoundResult>(await Controller().SetRole("x", Roles.Teacher, true));
    }
}

public class AdminRoleNamesTests
{
    [Theory]
    [InlineData(Roles.Administrator, "Администратор")]
    [InlineData(Roles.Teacher, "Учител")]
    [InlineData(Roles.Student, "Ученик")]
    [InlineData(Roles.Parent, "Родител")]
    [InlineData(Roles.User, "User")]
    [InlineData("Нещо", "Нещо")]
    public void Label_IsBulgarianForTheAssignableRoles_AndTheNameForTheOthers(string role, string expected)
    {
        Assert.Equal(expected, RoleNames.Label(role));
    }
}
