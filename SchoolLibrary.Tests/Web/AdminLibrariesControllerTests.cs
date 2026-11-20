using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Moq;
using SchoolLibrary.Areas.Admin.Controllers;
using SchoolLibrary.Areas.Admin.Models;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.OnlineLibraries;

namespace SchoolLibrary.Tests.Web;

public class AdminLibrariesControllerTests
{
    private readonly Mock<IOnlineLibraryService> _service = new();
    private readonly Mock<ILibraryIconService> _icons = new();

    public AdminLibrariesControllerTests()
    {
        _service.Setup(s => s.GetIcons()).Returns(new[] { new LibraryIconOption("bi-book", "Книга"), new LibraryIconOption("bi-feather", "Перо") });
        _icons.Setup(i => i.RefreshAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(Ok());
    }

    private LibrariesController Controller() => new LibrariesController(_service.Object, _icons.Object).WithUser("admin", Roles.Administrator);

    private static OnlineLibraryFormViewModel Form(int id = 3) =>
        new() { Id = id, Name = "Читанка", Description = "d", Url = "https://chitanka.info", Icon = "bi-feather", SortOrder = 10 };

    private static ServiceResult<OnlineLibrary> Ok() =>
        ServiceResult<OnlineLibrary>.Success(new OnlineLibrary { Id = 3, Name = "Читанка", Url = "https://chitanka.info" });

    private static ServiceResult<OnlineLibrary> Fail(ServiceError error, string message = "грешка") =>
        ServiceResult<OnlineLibrary>.Failure(error, message);

    [Fact]
    public async Task Index_ListsTheLibraries()
    {
        var libraries = new List<OnlineLibrary> { new() { Name = "А" }, new() { Name = "Б" } };
        _service.Setup(s => s.GetAllAsync()).ReturnsAsync(libraries);

        var result = await Controller().Index();

        Assert.Equal(libraries, Assert.IsAssignableFrom<IEnumerable<OnlineLibrary>>(Assert.IsType<ViewResult>(result).Model));
    }

    [Fact]
    public void Create_Get_ShowsAnEmptyFormWithTheIconChoices()
    {
        var controller = Controller();

        var result = controller.Create();

        Assert.IsType<OnlineLibraryFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        IEnumerable<SelectListItem> icons = Assert.IsAssignableFrom<IEnumerable<SelectListItem>>((object)controller.ViewBag.Icons);
        Assert.Equal(new[] { "bi-book", "bi-feather" }, icons.Select(i => i.Value));
    }

    [Fact]
    public async Task Create_Post_Success_SendsTheFormFieldsToTheService_AndGoesToTheList()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<OnlineLibraryInput>())).ReturnsAsync(Ok());
        var controller = Controller();

        var result = await controller.Create(Form());

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("Читанка", controller.TempDataText("SuccessMessage"));
        _service.Verify(s => s.CreateAsync(new OnlineLibraryInput("Читанка", "d", "https://chitanka.info", "bi-feather", 10)), Times.Once);
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ShowsTheFormAgainWithoutSaving()
    {
        var controller = Controller();
        controller.ModelState.AddModelError("Name", "Името на библиотеката е задължително");

        var result = await controller.Create(new OnlineLibraryFormViewModel());

        Assert.IsType<ViewResult>(result);
        Assert.NotNull(controller.ViewBag.Icons);
        _service.Verify(s => s.CreateAsync(It.IsAny<OnlineLibraryInput>()), Times.Never);
    }

    [Theory]
    [InlineData(ServiceError.Conflict, "Библиотека с име Читанка вече съществува.")]
    [InlineData(ServiceError.Invalid, "Уеб адресът трябва да започва с http:// или https://.")]
    public async Task Create_Post_AServiceError_ShowsTheMessageOnTheForm(ServiceError error, string message)
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<OnlineLibraryInput>())).ReturnsAsync(Fail(error, message));
        var controller = Controller();

        var result = await controller.Create(Form());

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, e => e.ErrorMessage == message);
    }

    [Theory]
    [InlineData(null, typeof(NotFoundResult))]
    [InlineData(99, typeof(NotFoundResult))]
    [InlineData(3, typeof(ViewResult))]
    public async Task Edit_Get_NeedsAnExistingLibrary(int? id, Type expected)
    {
        _service.Setup(s => s.GetByIdAsync(3)).ReturnsAsync(new OnlineLibrary { Id = 3, Name = "Читанка", Url = "https://chitanka.info" });

        Assert.IsType(expected, await Controller().Edit(id));
    }

    [Fact]
    public async Task Edit_Get_FillsTheFormWithTheLibrary()
    {
        _service.Setup(s => s.GetByIdAsync(3)).ReturnsAsync(
            new OnlineLibrary { Id = 3, Name = "Читанка", Description = "d", Url = "https://chitanka.info", Icon = "bi-feather", SortOrder = 7 });

        var result = await Controller().Edit(3);

        var form = Assert.IsType<OnlineLibraryFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal((3, "Читанка", "d", "https://chitanka.info", "bi-feather", 7), (form.Id, form.Name, form.Description, form.Url, form.Icon, form.SortOrder));
    }

    [Fact]
    public async Task Edit_Post_AnotherId_ReturnsNotFound()
    {
        Assert.IsType<NotFoundResult>(await Controller().Edit(3, Form(id: 4)));
    }

    [Fact]
    public async Task Edit_Post_Success_SaysSo_AndGoesToTheList()
    {
        _service.Setup(s => s.UpdateAsync(3, It.IsAny<OnlineLibraryInput>())).ReturnsAsync(Ok());
        var controller = Controller();

        var result = await controller.Edit(3, Form());

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("променена", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Edit_Post_InvalidModel_ShowsTheFormAgain()
    {
        var controller = Controller();
        controller.ModelState.AddModelError("Name", "грешка");

        Assert.IsType<ViewResult>(await controller.Edit(3, Form()));
        _service.Verify(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<OnlineLibraryInput>()), Times.Never);
    }

    [Fact]
    public async Task Edit_Post_UnknownLibrary_ReturnsNotFound()
    {
        _service.Setup(s => s.UpdateAsync(3, It.IsAny<OnlineLibraryInput>())).ReturnsAsync(Fail(ServiceError.NotFound));

        Assert.IsType<NotFoundResult>(await Controller().Edit(3, Form()));
    }

    [Fact]
    public async Task Edit_Post_AServiceError_ShowsTheMessageOnTheForm()
    {
        _service.Setup(s => s.UpdateAsync(3, It.IsAny<OnlineLibraryInput>())).ReturnsAsync(Fail(ServiceError.Conflict, "вече съществува"));
        var controller = Controller();

        var result = await controller.Edit(3, Form());

        Assert.IsType<ViewResult>(result);
        Assert.Single(controller.ModelState[string.Empty]!.Errors);
    }

    [Theory]
    [InlineData(null, typeof(NotFoundResult))]
    [InlineData(99, typeof(NotFoundResult))]
    [InlineData(3, typeof(ViewResult))]
    public async Task Delete_Get_NeedsAnExistingLibrary(int? id, Type expected)
    {
        _service.Setup(s => s.GetByIdAsync(3)).ReturnsAsync(new OnlineLibrary { Id = 3, Name = "Читанка" });

        Assert.IsType(expected, await Controller().Delete(id));
    }

    [Fact]
    public async Task DeleteConfirmed_Success_SaysSo()
    {
        _service.Setup(s => s.DeleteAsync(3)).ReturnsAsync(Ok());
        var controller = Controller();

        var result = await controller.DeleteConfirmed(3);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("премахната", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownLibrary_ReturnsNotFound()
    {
        _service.Setup(s => s.DeleteAsync(3)).ReturnsAsync(Fail(ServiceError.NotFound));

        Assert.IsType<NotFoundResult>(await Controller().DeleteConfirmed(3));
    }

    private const string IconNote = "Иконата на сайта не можа да бъде изтеглена. Показва се избраната иконка.";

    [Fact]
    public async Task Create_Post_Success_DownloadsTheSiteIconOfTheNewLibrary()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<OnlineLibraryInput>())).ReturnsAsync(Ok());
        var controller = Controller();

        await controller.Create(Form());

        _icons.Verify(i => i.RefreshAsync(3, false, It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain("икона", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Create_Post_NoSiteIcon_StillSaves_AndAddsANote()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<OnlineLibraryInput>())).ReturnsAsync(Ok());
        _icons.Setup(i => i.RefreshAsync(3, false, It.IsAny<CancellationToken>())).ReturnsAsync(Fail(ServiceError.Invalid, IconNote));
        var controller = Controller();

        var result = await controller.Create(Form());

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal("Добавена е библиотека Читанка. " + IconNote, controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Create_Post_AServiceError_DownloadsNoIcon()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<OnlineLibraryInput>())).ReturnsAsync(Fail(ServiceError.Conflict));

        await Controller().Create(Form());

        _icons.Verify(i => i.RefreshAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Edit_Post_Success_RefreshesTheIconOnlyWhenNeeded()
    {
        _service.Setup(s => s.UpdateAsync(3, It.IsAny<OnlineLibraryInput>())).ReturnsAsync(Ok());

        await Controller().Edit(3, Form());

        // Not forced: the service downloads again only when the address changed or there is no icon
        _icons.Verify(i => i.RefreshAsync(3, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteConfirmed_Success_RemovesTheStoredIcon()
    {
        var deleted = new OnlineLibrary { Id = 3, Name = "Читанка", SiteIconKey = "libraries/a.png" };
        _service.Setup(s => s.DeleteAsync(3)).ReturnsAsync(ServiceResult<OnlineLibrary>.Success(deleted));

        await Controller().DeleteConfirmed(3);

        _icons.Verify(i => i.DeleteStoredAsync(deleted, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownLibrary_RemovesNoIcon()
    {
        _service.Setup(s => s.DeleteAsync(3)).ReturnsAsync(Fail(ServiceError.NotFound));

        await Controller().DeleteConfirmed(3);

        _icons.Verify(i => i.DeleteStoredAsync(It.IsAny<OnlineLibrary>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshIcon_Success_ForcesTheDownload_AndSaysSo()
    {
        var controller = Controller();

        var result = await controller.RefreshIcon(3);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal("Иконата на Читанка е обновена.", controller.TempDataText("SuccessMessage"));
        _icons.Verify(i => i.RefreshAsync(3, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshIcon_NoIcon_ShowsTheError()
    {
        _icons.Setup(i => i.RefreshAsync(3, true, It.IsAny<CancellationToken>())).ReturnsAsync(Fail(ServiceError.Invalid, IconNote));
        var controller = Controller();

        var result = await controller.RefreshIcon(3);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(IconNote, controller.TempDataText("ErrorMessage"));
    }

    [Fact]
    public async Task RefreshIcon_UnknownLibrary_ReturnsNotFound()
    {
        _icons.Setup(i => i.RefreshAsync(99, true, It.IsAny<CancellationToken>())).ReturnsAsync(Fail(ServiceError.NotFound));

        Assert.IsType<NotFoundResult>(await Controller().RefreshIcon(99));
    }

    [Fact]
    public void RefreshIcon_IsAPostWithAntiforgery()
    {
        var method = typeof(LibrariesController).GetMethod(nameof(LibrariesController.RefreshIcon))!;

        Assert.NotEmpty(method.GetCustomAttributes(typeof(HttpPostAttribute), false));
        Assert.NotEmpty(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), false));
    }
}
