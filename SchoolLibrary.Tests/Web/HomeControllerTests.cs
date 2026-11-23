using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Favorites;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

public class HomeControllerTests
{
    private readonly Mock<IResourceService> _resources = new();
    private readonly Mock<IFavoriteService> _favorites = new();
    private ResourceFilter? _filter;

    public HomeControllerTests()
    {
        _resources.Setup(r => r.SearchAsync(It.IsAny<ResourceFilter>()))
            .Callback<ResourceFilter>(f => _filter = f)
            .ReturnsAsync(new PagedResult<Resource>(
                new List<Resource> { new() { Id = 1, Title = "А" }, new() { Id = 2, Title = "Б" } }, 40, 1, HomeIndexViewModel.NewestCount));
    }

    private Task<IActionResult> Index(string? userId, params string[] roles) =>
        new HomeController(NullLogger<HomeController>.Instance).WithUser(userId, roles).Index(_resources.Object, _favorites.Object);

    [Fact]
    public async Task Index_ShowsTheNewestFew_TheVisitorMaySee()
    {
        var result = await Index(userId: null);

        var model = Assert.IsType<HomeIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(new[] { 1, 2 }, model.Newest.Select(r => r.Id));
        Assert.Equal(40, model.TotalCount);
        Assert.Equal(HomeIndexViewModel.NewestCount, _filter!.PageSize);
        Assert.Equal(1, _filter.Page);
        Assert.Equal(ResourceSortOrder.DateDesc, _filter.SortOrder);
        Assert.Equal(ResourceViewer.Anonymous, _filter.Viewer);
    }

    [Fact]
    public async Task Index_ForAVisitor_DoesNotLookUpFavorites()
    {
        var result = await Index(userId: null);

        Assert.Empty(Assert.IsType<HomeIndexViewModel>(Assert.IsType<ViewResult>(result).Model).FavoriteIds);
        _favorites.Verify(f => f.GetFavoriteIdsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<int>>()), Times.Never);
    }

    [Fact]
    public async Task Index_ForASignedInUser_ListsWhatTheyMaySee_AndMarksTheirFavorites()
    {
        _favorites.Setup(f => f.GetFavoriteIdsAsync("u1", It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 1, 2 }))))
            .ReturnsAsync(new HashSet<int> { 2 });

        var result = await Index("u1", Roles.Teacher);

        var model = Assert.IsType<HomeIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(new[] { 2 }, model.FavoriteIds);
        Assert.Equal("u1", _filter!.Viewer.UserId);
        Assert.True(_filter.Viewer.Audience.HasFlag(ResourceAudience.Teachers));
    }
}
