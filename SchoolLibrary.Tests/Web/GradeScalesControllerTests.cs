using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Exams;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

public class GradeScalesControllerTests
{
    private readonly Mock<IGradeScaleService> _scales = new();

    private GradeScalesController Controller(string userId = "t1", params string[] roles) =>
        new GradeScalesController(_scales.Object).WithUser(userId, roles.Length == 0 ? new[] { Roles.Teacher } : roles);

    private static GradeScale Scale(int id = 4) => new() { Id = id, Name = "Строга", Three = 40m, Four = 60m, Five = 75m, Six = 90m, OwnerId = "t1" };

    private static GradeScaleFormViewModel Form() => new() { Name = "Строга", Three = "40", Four = "60,5", Five = "75.25", Six = "90" };

    // ---------- Index ----------

    [Fact]
    public async Task Index_ForATeacher_ShowsTheirScales_AndForAnAdministratorEveryones()
    {
        _scales.Setup(s => s.GetForOwnerAsync(It.IsAny<string?>())).ReturnsAsync(new List<GradeScaleListItem> { new(Scale(), 2) });

        var view = Assert.IsType<ViewResult>(await Controller().Index());
        await Controller("a1", Roles.Administrator).Index();

        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<GradeScaleListItem>>(view.Model));
        _scales.Verify(s => s.GetForOwnerAsync("t1"), Times.Once);
        _scales.Verify(s => s.GetForOwnerAsync(null), Times.Once);
    }

    // ---------- Create ----------

    [Fact]
    public void Create_Get_StartsFromTheSystemThresholds()
    {
        var model = Assert.IsType<GradeScaleFormViewModel>(Assert.IsType<ViewResult>(Controller().Create()).Model);

        Assert.Equal(("30", "50", "70", "85"), (model.Three, model.Four, model.Five, model.Six));
    }

    [Fact]
    public async Task Create_Valid_SavesForTheTeacher_AndGoesToTheList()
    {
        _scales.Setup(s => s.CreateAsync(It.IsAny<GradeScaleInput>(), "t1")).ReturnsAsync(ServiceResult<GradeScale>.Success(Scale()));
        var controller = Controller();

        var result = await controller.Create(Form());

        Assert.Equal(nameof(GradeScalesController.Index), Assert.IsType<RedirectToActionResult>(result).ActionName);
        _scales.Verify(s => s.CreateAsync(new GradeScaleInput("Строга", 40m, 60.5m, 75.25m, 90m), "t1"), Times.Once);   // comma or point
        Assert.Equal("Скалата „Строга“ е създадена.", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Create_TakenName_ShowsTheErrorAtTheName()
    {
        _scales.Setup(s => s.CreateAsync(It.IsAny<GradeScaleInput>(), "t1"))
            .ReturnsAsync(ServiceResult<GradeScale>.Failure(ServiceError.Conflict, "Вече имате скала „Строга“."));
        var controller = Controller();

        var view = Assert.IsType<ViewResult>(await controller.Create(Form()));

        Assert.Equal("Form", view.ViewName);
        Assert.Equal("Вече имате скала „Строга“.", controller.ModelState[nameof(GradeScaleFormViewModel.Name)]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task Create_InvalidModel_DoesNotCallTheService()
    {
        var controller = Controller();
        controller.ModelState.AddModelError(nameof(GradeScaleFormViewModel.Name), "Въведете име на скалата");

        Assert.IsType<ViewResult>(await controller.Create(Form()));
        _scales.VerifyNoOtherCalls();
    }

    // ---------- Edit ----------

    [Fact]
    public async Task Edit_Get_FillsTheForm()
    {
        _scales.Setup(s => s.GetAsync(4, "t1", false)).ReturnsAsync(ServiceResult<GradeScale>.Success(Scale()));
        var controller = Controller();

        var view = Assert.IsType<ViewResult>(await controller.Edit(4));

        var model = Assert.IsType<GradeScaleFormViewModel>(view.Model);
        Assert.Equal(("Строга", "40", "90"), (model.Name, model.Three, model.Six));
        Assert.Equal(4, controller.ViewData["ScaleId"]);
    }

    [Theory]
    [InlineData(ServiceError.NotFound, typeof(NotFoundResult))]
    [InlineData(ServiceError.Forbidden, typeof(ForbidResult))]
    public async Task Edit_Refused_MapsToStatusResults(ServiceError error, Type expected)
    {
        _scales.Setup(s => s.GetAsync(4, "t1", false)).ReturnsAsync(ServiceResult<GradeScale>.Failure(error, "грешка"));
        _scales.Setup(s => s.UpdateAsync(4, It.IsAny<GradeScaleInput>(), "t1", false)).ReturnsAsync(ServiceResult<GradeScale>.Failure(error, "грешка"));

        Assert.IsType(expected, await Controller().Edit(4));
        Assert.IsType(expected, await Controller().Edit(4, Form()));
    }

    [Fact]
    public async Task Edit_Post_InvalidThresholds_ShowTheMessageAboveTheForm()
    {
        _scales.Setup(s => s.UpdateAsync(4, It.IsAny<GradeScaleInput>(), "a1", true))
            .ReturnsAsync(ServiceResult<GradeScale>.Failure(ServiceError.Invalid, "Праговете трябва да са между 0 и 100%."));
        var controller = Controller("a1", Roles.Administrator);

        Assert.IsType<ViewResult>(await controller.Edit(4, Form()));

        Assert.Equal("Праговете трябва да са между 0 и 100%.", controller.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task Edit_Post_Saved_SaysTheGradesAreRecalculated()
    {
        _scales.Setup(s => s.UpdateAsync(4, It.IsAny<GradeScaleInput>(), "t1", false)).ReturnsAsync(ServiceResult<GradeScale>.Success(Scale()));
        var controller = Controller();

        Assert.IsType<RedirectToActionResult>(await controller.Edit(4, Form()));
        Assert.Equal("Скалата „Строга“ е запазена. Оценките на изпитите с нея са преизчислени.", controller.TempDataText("SuccessMessage"));
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_InUse_ShowsWhy()
    {
        _scales.Setup(s => s.DeleteAsync(4, "t1", false))
            .ReturnsAsync(ServiceResult<GradeScale>.Failure(ServiceError.Conflict, "Скалата се използва от 1 изпит."));
        var controller = Controller();

        Assert.IsType<RedirectToActionResult>(await controller.Delete(4));
        Assert.Equal("Скалата се използва от 1 изпит.", controller.TempDataText("ErrorMessage"));
    }

    [Fact]
    public async Task Delete_Done_SaysSo_AndAnotherTeachersScaleIsForbidden()
    {
        _scales.Setup(s => s.DeleteAsync(4, "t1", false)).ReturnsAsync(ServiceResult<GradeScale>.Success(Scale()));
        _scales.Setup(s => s.DeleteAsync(5, "t1", false)).ReturnsAsync(ServiceResult<GradeScale>.Failure(ServiceError.Forbidden, "грешка"));
        var controller = Controller();

        await controller.Delete(4);

        Assert.Equal("Скалата „Строга“ е изтрита.", controller.TempDataText("SuccessMessage"));
        Assert.IsType<ForbidResult>(await Controller().Delete(5));
    }

    // ---------- Form validation ----------

    [Fact]
    public void Form_ThresholdsNotRising_IsInvalid()
    {
        var form = Form();
        form.Five = "50";

        var errors = form.Validate(new ValidationContext(form)).ToList();

        Assert.Equal("Всеки праг трябва да е по-висок от предишния (за 3 < за 4 < за 5 < за 6).", Assert.Single(errors).ErrorMessage);
        Assert.Empty(Form().Validate(new ValidationContext(Form())));
    }

    [Fact]
    public void Form_ThresholdNotANumber_IsReportedAtItsField()
    {
        var form = Form();
        form.Four = "много";

        var error = Assert.Single(form.Validate(new ValidationContext(form)));

        Assert.Equal("Въведете прага за 4 като число, например 33,33.", error.ErrorMessage);
        Assert.Equal(new[] { nameof(GradeScaleFormViewModel.Four) }, error.MemberNames);
    }

    [Fact]
    public void Form_FromAScale_ShowsThresholdsWithAPoint()
    {
        var form = GradeScaleFormViewModel.From(new GradeScale { Name = "С", Three = 33.33m, Four = 50.00m, Five = 66.67m, Six = 90m });

        Assert.Equal(("33.33", "50", "66.67", "90"), (form.Three, form.Four, form.Five, form.Six));
    }
}
