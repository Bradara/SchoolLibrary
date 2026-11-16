using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Areas.Admin.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.GradeLevels;

namespace SchoolLibrary.Tests.Web;

public class AdminGradeLevelsControllerTests
{
    [Fact]
    public async Task Index_ListsTheGradesWithTheirResourceCounts()
    {
        var items = new List<GradeLevelListItem> { new(new GradeLevel { Number = 5 }, 3), new(new GradeLevel { Number = 6 }, 0) };
        var service = new Mock<IGradeLevelService>();
        service.Setup(s => s.GetAllWithCountsAsync()).ReturnsAsync(items);
        var controller = new GradeLevelsController(service.Object).WithUser("admin", Roles.Administrator);

        var result = await controller.Index();

        Assert.Equal(items, Assert.IsAssignableFrom<IEnumerable<GradeLevelListItem>>(Assert.IsType<ViewResult>(result).Model));
    }
}
