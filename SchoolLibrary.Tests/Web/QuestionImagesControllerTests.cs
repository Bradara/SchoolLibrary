using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

public class QuestionImagesControllerTests
{
    private const string Teacher = "teacher-1";
    private const string EditPage = "/Sections/EditQuestion/3?questionId=9";

    private readonly Mock<IQuestionImageService> _service = new();

    private QuestionImagesController Controller(bool admin = false) =>
        new QuestionImagesController(_service.Object)
            .WithUser(Teacher, admin ? new[] { Roles.Administrator } : new[] { Roles.Teacher });

    private static IFormFile Png() =>
        new FormFile(new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), 0, 4, "image", "stack.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

    private static QuestionImage Image(int number = 1) => new() { Id = 5, Number = number, QuestionId = 9 };

    [Fact]
    public async Task Upload_Valid_StoresAndGoesBackWithMarkerHint()
    {
        _service.Setup(s => s.AddAsync(9, It.IsAny<UploadedFile>(), "Стек", Teacher, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<QuestionImage>.Success(Image(2)));
        var controller = Controller();

        var result = await controller.Upload(9, Png(), "Стек", EditPage, CancellationToken.None);

        Assert.Equal(EditPage, Assert.IsType<LocalRedirectResult>(result).Url);
        Assert.Contains("[[img:2]]", controller.TempDataText("SuccessMessage"));
        _service.Verify(s => s.AddAsync(9, It.Is<UploadedFile>(f => f.FileName == "stack.png" && f.Length == 4),
            "Стек", Teacher, false, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Upload_NoFile_ShowsErrorWithoutCallingService()
    {
        var controller = Controller();

        var result = await controller.Upload(9, null, null, EditPage, CancellationToken.None);

        Assert.IsType<LocalRedirectResult>(result);
        Assert.NotNull(controller.TempDataText("ErrorMessage"));
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Upload_Invalid_ShowsServiceMessage()
    {
        _service.Setup(s => s.AddAsync(9, It.IsAny<UploadedFile>(), null, Teacher, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<QuestionImage>.Failure(ServiceError.Invalid, "Разрешени са само изображения PNG, JPEG, GIF и WebP."));
        var controller = Controller();

        await controller.Upload(9, Png(), null, EditPage, CancellationToken.None);

        Assert.Equal("Разрешени са само изображения PNG, JPEG, GIF и WebP.", controller.TempDataText("ErrorMessage"));
    }

    [Fact]
    public async Task Upload_Forbidden_ReturnsForbid()
    {
        _service.Setup(s => s.AddAsync(9, It.IsAny<UploadedFile>(), null, Teacher, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<QuestionImage>.Failure(ServiceError.Forbidden, "x"));

        Assert.IsType<ForbidResult>(await Controller().Upload(9, Png(), null, EditPage, CancellationToken.None));
    }

    [Fact]
    public async Task Upload_MissingQuestion_ReturnsNotFound()
    {
        _service.Setup(s => s.AddAsync(9, It.IsAny<UploadedFile>(), null, Teacher, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<QuestionImage>.Failure(ServiceError.NotFound, "x"));

        Assert.IsType<NotFoundResult>(await Controller().Upload(9, Png(), null, EditPage, CancellationToken.None));
    }

    [Fact]
    public async Task Administrator_IsPassedToService()
    {
        _service.Setup(s => s.DeleteAsync(5, Teacher, true)).ReturnsAsync(ServiceResult<QuestionImage>.Success(Image()));

        await Controller(admin: true).Delete(5, EditPage);

        _service.Verify(s => s.DeleteAsync(5, Teacher, true));
    }

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("//evil.example")]
    [InlineData(null)]
    public async Task Delete_ForeignOrMissingReturnUrl_GoesToTheBank(string? returnUrl)
    {
        _service.Setup(s => s.DeleteAsync(5, Teacher, false)).ReturnsAsync(ServiceResult<QuestionImage>.Success(Image()));
        var controller = Controller();

        var result = await controller.Delete(5, returnUrl);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Sections", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Изображението е премахнато.", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task UpdateAltText_Saves_AndGoesBack()
    {
        _service.Setup(s => s.UpdateAltTextAsync(5, "Опашка", Teacher, false)).ReturnsAsync(ServiceResult<QuestionImage>.Success(Image()));
        var controller = Controller();

        var result = await controller.UpdateAltText(5, "Опашка", EditPage);

        Assert.Equal(EditPage, Assert.IsType<LocalRedirectResult>(result).Url);
        Assert.Equal("Описанието е запазено.", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task UpdateAltText_TooLong_ShowsError()
    {
        _service.Setup(s => s.UpdateAltTextAsync(5, It.IsAny<string>(), Teacher, false))
            .ReturnsAsync(ServiceResult<QuestionImage>.Failure(ServiceError.Invalid, "Твърде дълго."));
        var controller = Controller();

        await controller.UpdateAltText(5, new string('а', 300), EditPage);

        Assert.Equal("Твърде дълго.", controller.TempDataText("ErrorMessage"));
    }

    [Fact]
    public void Controller_IsForTeachersAndAdministrators()
    {
        var attribute = (Microsoft.AspNetCore.Authorization.AuthorizeAttribute)Attribute.GetCustomAttribute(
            typeof(QuestionImagesController), typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute))!;

        Assert.Equal(Roles.TeacherOrAdministrator, attribute.Roles);
    }

    [Fact]
    public void PanelModel_OrdersByNumber_AndStopsAtTheLimit()
    {
        var question = new Question { Id = 9, Images = { Image(3), Image(1) } };
        var full = new Question { Id = 9 };
        for (var n = 1; n <= QuestionImage.MaxPerQuestion; n++)
        {
            full.Images.Add(Image(n));
        }

        var panel = QuestionImagesPanelModel.For(question);

        Assert.Equal(new[] { 1, 3 }, panel.Images.Select(i => i.Number));
        Assert.True(panel.CanAddMore);
        Assert.False(QuestionImagesPanelModel.For(full).CanAddMore);
    }
}
