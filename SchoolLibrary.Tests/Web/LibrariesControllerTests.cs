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

        var result = await new LibrariesController(service.Object).Index();

        Assert.Equal(libraries, Assert.IsAssignableFrom<IEnumerable<OnlineLibrary>>(Assert.IsType<ViewResult>(result).Model));
    }

    [Fact]
    public void TheCatalogNeedsASignedInUser()
    {
        Assert.NotEmpty(typeof(LibrariesController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }
}
