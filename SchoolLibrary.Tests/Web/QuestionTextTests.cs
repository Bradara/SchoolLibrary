using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Extensions;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

/// <summary>Showing the pictures of a question in its text.</summary>
public class QuestionTextTests
{
    private static readonly Guid FileId = Guid.Parse("0123456789abcdef0123456789abcdef");

    private static QuestionImage Image(int number, string? alt = null, Guid? fileId = null) => new()
    {
        Number = number,
        StorageKey = $"{QuestionImageFiles.KeyPrefix}{(fileId ?? FileId):N}.png",
        FileName = "image.png",
        ContentType = "image/png",
        SizeBytes = 8,
        AltText = alt
    };

    private static Question Question(string text, string? statement = null, params QuestionImage[] images) => new()
    {
        Text = text,
        Statement = statement,
        Type = statement == null ? QuestionType.SingleChoice : QuestionType.Code,
        Images = images.ToList()
    };

    // ---------- splitting ----------

    [Fact]
    public void Split_CutsTextAtMarkers()
    {
        var image = Image(1);

        var parts = QuestionImageMarkers.Split("Виж [[img:1]] и отговори", new[] { image });

        Assert.Equal(3, parts.Count);
        Assert.Equal("Виж ", parts[0].Text);
        Assert.Same(image, parts[1].Image);
        Assert.Equal(" и отговори", parts[2].Text);
    }

    [Fact]
    public void Split_MarkerWithoutPicture_IsDropped()
    {
        var parts = QuestionImageMarkers.Split("а [[img:3]] б", Array.Empty<QuestionImage>());

        Assert.Equal(new[] { "а ", " б" }, parts.Select(p => p.Text));
    }

    [Fact]
    public void Split_EmptyText_HasNoParts()
    {
        Assert.Empty(QuestionImageMarkers.Split(null, new[] { Image(1) }));
    }

    [Fact]
    public void Unplaced_ReturnsPicturesWithoutMarkerInAnyText()
    {
        var unplaced = QuestionImageMarkers.Unplaced(new[] { Image(3), Image(1), Image(2) }, "[[img:1]]", "[[img:2]]");

        Assert.Equal(new[] { 3 }, unplaced.Select(i => i.Number));
    }

    [Fact]
    public void ToPlainText_ReplacesMarkers()
    {
        Assert.Equal("Виж [изображение]", QuestionImageMarkers.ToPlainText("Виж [[img:1]]"));
        Assert.Equal(string.Empty, QuestionImageMarkers.ToPlainText(null));
    }

    // ---------- where the unplaced pictures go ----------

    [Fact]
    public void Model_QuestionWithoutTask_ShowsUnplacedAfterText()
    {
        var question = Question("Текст", null, Image(1));

        Assert.Single(QuestionTextModel.ForText(question).After);
        Assert.Empty(QuestionTextModel.ForStatement(question).After);
    }

    [Fact]
    public void Model_CodeQuestionWithTask_ShowsUnplacedAfterTask()
    {
        var question = Question("Сума", "Прочетете [[img:1]]", Image(1), Image(2));

        Assert.Empty(QuestionTextModel.ForText(question).After);
        var statement = QuestionTextModel.ForStatement(question);
        Assert.Equal(new[] { 2 }, statement.After.Select(i => i.Number));
        Assert.Contains(statement.Parts, p => p.Image?.Number == 1);
    }

    // ---------- HTML ----------

    private static IHtmlHelper Html()
    {
        var url = new Mock<IUrlHelper>();
        url.Setup(u => u.Action(It.IsAny<UrlActionContext>()))
            .Returns((UrlActionContext c) => $"/Quizzes/Image/{new RouteValueReader(c.Values).Id}");
        var factory = new Mock<IUrlHelperFactory>();
        factory.Setup(f => f.GetUrlHelper(It.IsAny<Microsoft.AspNetCore.Mvc.ActionContext>())).Returns(url.Object);

        var services = new ServiceCollection().AddSingleton(factory.Object).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var viewContext = new ViewContext { HttpContext = http };

        var html = new Mock<IHtmlHelper>();
        html.SetupGet(h => h.ViewContext).Returns(viewContext);
        return html.Object;
    }

    private static string Render(IHtmlContent content)
    {
        using var writer = new StringWriter();
        // Cyrillic is left as is, the way the site encodes its pages
        content.WriteTo(writer, HtmlEncoder.Create(UnicodeRanges.All));
        return writer.ToString();
    }

    [Fact]
    public void Html_PutsImageInPlaceOfMarker()
    {
        var html = Render(Html().QuestionText(Question("Виж [[img:1]] тук", null, Image(1, "Стек"))));

        Assert.Equal(
            $"Виж <img alt=\"Стек\" class=\"question-image\" loading=\"lazy\" src=\"/Quizzes/Image/{FileId:N}\" /> тук",
            html);
    }

    [Fact]
    public void Html_EncodesText_SoNoMarkupGetsThrough()
    {
        var html = Render(Html().QuestionText(Question("<script>alert(1)</script> [[img:1]]", null,
            Image(1, "\" onerror=\"alert(1)"))));

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("\" onerror=", html);
    }

    [Fact]
    public void Html_NoDescription_UsesNumberedAlt_AndCompactClass()
    {
        var html = Render(Html().QuestionText(Question("[[img:2]]", null, Image(2)), compact: true));

        Assert.Contains("question-image is-compact", html);
        Assert.Contains("alt=\"Изображение 2\"", html);
    }

    [Fact]
    public void Html_ForeignStorageKey_IsSkipped()
    {
        var image = Image(1);
        image.StorageKey = "resources/x.pdf";

        var html = Render(Html().QuestionText(Question("[[img:1]]", null, image)));

        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void Html_Statement_ShowsUnplacedAfterTask()
    {
        var question = Question("Сума", "Задача", Image(1));

        var html = Render(Html().QuestionStatement(question));

        Assert.Contains("<img", html);
        Assert.DoesNotContain("<img", Render(Html().QuestionText(question)));
    }

    // ---------- serving ----------

    private static QuizzesController Controller() =>
        new QuizzesController(Mock.Of<IQuizService>()).WithUser(null);

    [Fact]
    public async Task Image_KnownFile_ReturnsBytesWithTypeAndCaching()
    {
        var service = new Mock<IQuestionImageService>();
        service.Setup(s => s.OpenAsync(FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredQuestionImage(new MemoryStream(new byte[] { 1, 2 }), "image/png"));
        var controller = Controller();

        var result = await controller.Image(FileId.ToString("N"), service.Object, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("private, max-age=86400", controller.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("../../etc/passwd")]
    [InlineData("0123456789abcdef0123456789abcdeX")]
    public async Task Image_BadId_IsNotFound_WithoutLookup(string id)
    {
        var service = new Mock<IQuestionImageService>(MockBehavior.Strict);

        var result = await Controller().Image(id, service.Object, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Image_UnknownFile_IsNotFound()
    {
        var service = new Mock<IQuestionImageService>();

        var result = await Controller().Image(Guid.NewGuid().ToString("N"), service.Object, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // Reads the "id" route value of the anonymous object the helper passes to Url.Action
    private sealed class RouteValueReader(object? values)
    {
        public string? Id => values?.GetType().GetProperty("id")?.GetValue(values)?.ToString();
    }
}
