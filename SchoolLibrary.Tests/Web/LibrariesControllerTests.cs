using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.OnlineLibraries;

namespace SchoolLibrary.Tests.Web;

public class LibrariesControllerTests
{
    [Fact]
    public async Task Index_ShowsTheCatalogInTheServiceOrder()
    {
        var libraries = new List<OnlineLibrary> { new() { Name = "А" }, new() { Name = "Б" } };
        var service = new Mock<IOnlineLibraryService>();
        service.Setup(s => s.GetAllAsync()).ReturnsAsync(libraries);

        var result = await new LibrariesController(service.Object, Mock.Of<ILibraryIconService>()).Index();

        Assert.Equal(libraries, Assert.IsAssignableFrom<IEnumerable<OnlineLibrary>>(Assert.IsType<ViewResult>(result).Model));
    }

    [Fact]
    public void TheCatalogNeedsASignedInUser()
    {
        Assert.NotEmpty(typeof(LibrariesController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }

    [Fact]
    public async Task Icon_SendsTheStoredImage()
    {
        var icons = new Mock<ILibraryIconService>();
        var content = new MemoryStream(new byte[] { 1, 2, 3 });
        icons.Setup(i => i.OpenAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(new LibraryIconFile(content, "image/png"));

        var result = await new LibrariesController(Mock.Of<IOnlineLibraryService>(), icons.Object).Icon(5, default);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Same(content, file.FileStream);
        Assert.Equal("image/png", file.ContentType);
    }

    [Fact]
    public async Task Icon_ALibraryWithoutIcon_ReturnsNotFound()
    {
        var result = await new LibrariesController(Mock.Of<IOnlineLibraryService>(), Mock.Of<ILibraryIconService>()).Icon(5, default);

        Assert.IsType<NotFoundResult>(result);
    }
}
