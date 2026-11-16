using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Areas.Admin.Controllers;
using SchoolLibrary.Areas.Admin.Models;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Licenses;

namespace SchoolLibrary.Tests.Web;

public class AdminLicensesControllerTests
{
    private readonly Mock<ILicenseService> _service = new();

    private LicensesController Controller() => new LicensesController(_service.Object).WithUser("admin", Roles.Administrator);

    private static LicenseFormViewModel Form(int id = 3, string name = "MIT") => new() { Id = id, Name = name, Description = "d", Url = "https://e.com" };

    private static ServiceResult<License> Ok(string name = "MIT") => ServiceResult<License>.Success(new License { Id = 3, Name = name });

    private static ServiceResult<License> Fail(ServiceError error, string message = "грешка") => ServiceResult<License>.Failure(error, message);

    [Fact]
    public async Task Index_ListsTheLicensesWithTheirResourceCounts()
    {
        var items = new List<LicenseListItem> { new(new License { Name = "А" }, 2), new(new License { Name = "Б" }, 0) };
        _service.Setup(s => s.GetAllAsync()).ReturnsAsync(items);

        var result = await Controller().Index();

        Assert.Equal(items, Assert.IsAssignableFrom<IEnumerable<LicenseListItem>>(Assert.IsType<ViewResult>(result).Model));
    }

    [Fact]
    public void Create_Get_ShowsAnEmptyForm()
    {
        var result = Controller().Create();

        Assert.IsType<LicenseFormViewModel>(Assert.IsType<ViewResult>(result).Model);
    }

    [Fact]
    public async Task Create_Post_Success_SendsTheFormFieldsToTheService_AndGoesToTheList()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<LicenseInput>())).ReturnsAsync(Ok());
        var controller = Controller();

        var result = await controller.Create(Form());

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("MIT", controller.TempDataText("SuccessMessage"));
        _service.Verify(s => s.CreateAsync(new LicenseInput("MIT", "d", "https://e.com")), Times.Once);
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ShowsTheFormAgainWithoutSaving()
    {
        var controller = Controller();
        controller.ModelState.AddModelError("Name", "Името на лиценза е задължително");

        var result = await controller.Create(new LicenseFormViewModel());

        Assert.IsType<ViewResult>(result);
        _service.Verify(s => s.CreateAsync(It.IsAny<LicenseInput>()), Times.Never);
    }

    [Fact]
    public async Task Create_Post_ANameThatIsTaken_ShowsTheMessageOnTheNameField()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<LicenseInput>())).ReturnsAsync(Fail(ServiceError.Conflict, "Лиценз с име MIT вече съществува."));
        var controller = Controller();

        var result = await controller.Create(Form());

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState["Name"]!.Errors, e => e.ErrorMessage.Contains("вече съществува"));
    }

    [Fact]
    public async Task Create_Post_AnInvalidAddress_ShowsTheMessageOnTheForm()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<LicenseInput>())).ReturnsAsync(Fail(ServiceError.Invalid, "Уеб адресът трябва да започва с http:// или https://."));
        var controller = Controller();

        var result = await controller.Create(Form());

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, e => e.ErrorMessage.Contains("http"));
    }

    [Theory]
    [InlineData(null, typeof(NotFoundResult))]
    [InlineData(99, typeof(NotFoundResult))]
    [InlineData(3, typeof(ViewResult))]
    public async Task Edit_Get_NeedsAnExistingLicense(int? id, Type expected)
    {
        _service.Setup(s => s.GetByIdAsync(3)).ReturnsAsync(new License { Id = 3, Name = "MIT", Description = "d", Url = "https://e.com" });

        var result = await Controller().Edit(id);

        Assert.IsType(expected, result);
    }

    [Fact]
    public async Task Edit_Get_FillsTheFormWithTheLicense()
    {
        _service.Setup(s => s.GetByIdAsync(3)).ReturnsAsync(new License { Id = 3, Name = "MIT", Description = "d", Url = "https://e.com" });

        var result = await Controller().Edit(3);

        var form = Assert.IsType<LicenseFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal((3, "MIT", "d", "https://e.com"), (form.Id, form.Name, form.Description, form.Url));
    }

    [Fact]
    public async Task Edit_Post_AnotherId_ReturnsNotFound()
    {
        Assert.IsType<NotFoundResult>(await Controller().Edit(3, Form(id: 4)));
    }

    [Fact]
    public async Task Edit_Post_Success_SaysSo_AndGoesToTheList()
    {
        _service.Setup(s => s.UpdateAsync(3, It.IsAny<LicenseInput>())).ReturnsAsync(Ok());
        var controller = Controller();

        var result = await controller.Edit(3, Form());

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("променен", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Edit_Post_InvalidModel_ShowsTheFormAgain()
    {
        var controller = Controller();
        controller.ModelState.AddModelError("Name", "Името на лиценза е задължително");

        Assert.IsType<ViewResult>(await controller.Edit(3, Form()));
        _service.Verify(s => s.UpdateAsync(It.IsAny<int>(), It.IsAny<LicenseInput>()), Times.Never);
    }

    [Fact]
    public async Task Edit_Post_UnknownLicense_ReturnsNotFound()
    {
        _service.Setup(s => s.UpdateAsync(3, It.IsAny<LicenseInput>())).ReturnsAsync(Fail(ServiceError.NotFound));

        Assert.IsType<NotFoundResult>(await Controller().Edit(3, Form()));
    }

    [Fact]
    public async Task Edit_Post_ANameThatIsTaken_ShowsTheMessageOnTheNameField()
    {
        _service.Setup(s => s.UpdateAsync(3, It.IsAny<LicenseInput>())).ReturnsAsync(Fail(ServiceError.Conflict, "вече съществува"));
        var controller = Controller();

        var result = await controller.Edit(3, Form());

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState["Name"]!.Errors.Count > 0);
    }

    [Theory]
    [InlineData(null, typeof(NotFoundResult))]
    [InlineData(99, typeof(NotFoundResult))]
    [InlineData(3, typeof(ViewResult))]
    public async Task Delete_Get_NeedsAnExistingLicense(int? id, Type expected)
    {
        _service.Setup(s => s.GetByIdAsync(3)).ReturnsAsync(new License { Id = 3, Name = "MIT" });

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
    public async Task DeleteConfirmed_ALicenseInUse_ShowsWhy_AndKeepsIt()
    {
        _service.Setup(s => s.DeleteAsync(3)).ReturnsAsync(Fail(ServiceError.Conflict, "Лицензът MIT не може да бъде изтрит — използват го 4 ресурса."));
        var controller = Controller();

        var result = await controller.DeleteConfirmed(3);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("не може да бъде изтрит", controller.TempDataText("ErrorMessage"));
        Assert.Null(controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownLicense_ReturnsNotFound()
    {
        _service.Setup(s => s.DeleteAsync(3)).ReturnsAsync(Fail(ServiceError.NotFound));

        Assert.IsType<NotFoundResult>(await Controller().DeleteConfirmed(3));
    }
}
