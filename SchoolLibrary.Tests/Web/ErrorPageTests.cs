using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolLibrary.Controllers;
using SchoolLibrary.Models;

namespace SchoolLibrary.Tests.Web;

public class ErrorPageTests
{
    private static HomeController CreateController(string traceId = "trace-1")
    {
        var controller = new HomeController(NullLogger<HomeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { TraceIdentifier = traceId } }
        };
        return controller;
    }

    // ---------- the view model ----------

    [Fact]
    public void For404_HasItsOwnTextAndOffersLinks()
    {
        var model = ErrorViewModel.For(404, "abc");

        Assert.Equal(404, model.StatusCode);
        Assert.Equal("Страницата не е намерена", model.Title);
        Assert.True(model.IsNotFound);
        Assert.False(model.ShowRequestId);
    }

    [Theory]
    [InlineData(403, "Нямате достъп")]
    [InlineData(400, "Невалидна заявка")]
    [InlineData(500, "Нещо се обърка")]
    [InlineData(503, "Нещо се обърка")]
    public void ForKnownCodes_HasMatchingTitle(int code, string title)
    {
        var model = ErrorViewModel.For(code, "abc");

        Assert.Equal(code, model.StatusCode);
        Assert.Equal(title, model.Title);
        Assert.False(model.IsNotFound);
    }

    [Fact]
    public void ForOtherClientError_UsesTheGenericClientText()
    {
        var model = ErrorViewModel.For(418, "abc");

        Assert.Equal(418, model.StatusCode);
        Assert.Equal("Заявката не може да бъде изпълнена", model.Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(399)]
    [InlineData(600)]
    [InlineData(-5)]
    public void ForMissingOrOutOfRangeCode_IsShownAsServerError(int? code)
    {
        var model = ErrorViewModel.For(code, "abc");

        Assert.Equal(500, model.StatusCode);
        Assert.Equal("Нещо се обърка", model.Title);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(404, false)]
    [InlineData(403, false)]
    public void RequestId_IsShownOnlyForServerErrors(int code, bool shown)
    {
        Assert.Equal(shown, ErrorViewModel.For(code, "abc").ShowRequestId);
    }

    [Fact]
    public void ServerErrorWithoutRequestId_HidesTheRequestId()
    {
        Assert.False(ErrorViewModel.For(500, null).ShowRequestId);
        Assert.False(ErrorViewModel.For(500, "").ShowRequestId);
    }

    [Fact]
    public void EveryCode_HasTitleMessageAndIcon()
    {
        foreach (var code in new int?[] { null, 400, 403, 404, 418, 500, 503 })
        {
            var model = ErrorViewModel.For(code, "abc");

            Assert.False(string.IsNullOrWhiteSpace(model.Title));
            Assert.False(string.IsNullOrWhiteSpace(model.Message));
            Assert.StartsWith("bi-", model.Icon);
        }
    }

    [Fact]
    public void ErrorTexts_DoNotLeakTechnicalDetails()
    {
        foreach (var code in new int?[] { 400, 403, 404, 500 })
        {
            var model = ErrorViewModel.For(code, "abc");
            var text = model.Title + " " + model.Message;

            Assert.DoesNotContain("Exception", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("stack", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------- the controller action ----------

    [Fact]
    public void Error_WithCode404_ShowsThe404Page()
    {
        var result = CreateController().Error(404);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ErrorViewModel>(view.Model);
        Assert.Equal(404, model.StatusCode);
        Assert.True(model.IsNotFound);
    }

    [Fact]
    public void Error_WithoutCode_ShowsTheServerErrorPageWithTheTraceId()
    {
        var result = CreateController("trace-42").Error(null);

        var model = Assert.IsType<ErrorViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(500, model.StatusCode);
        Assert.Equal("trace-42", model.RequestId);
        Assert.True(model.ShowRequestId);
    }

    [Fact]
    public void Error_WithAnUnrelatedCode_DoesNotEchoItBack()
    {
        var result = CreateController().Error(200);

        var model = Assert.IsType<ErrorViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(500, model.StatusCode);
    }
}
