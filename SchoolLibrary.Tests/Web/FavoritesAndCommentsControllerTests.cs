using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Favorites;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

public class FavoritesControllerTests
{
    private readonly Mock<IFavoriteService> _service = new();

    private FavoritesController Controller(string? userId = "u1") =>
        new FavoritesController(_service.Object).WithUser(userId);

    [Fact]
    public async Task Index_ShowsThePageOfTheSignedInUser()
    {
        var items = new List<Resource> { new() { Id = 1, Title = "А" }, new() { Id = 2, Title = "Б" } };
        _service.Setup(s => s.GetFavoritesAsync("u1", 2, It.IsAny<int>()))
            .ReturnsAsync(new PagedResult<Resource>(items, totalCount: 12, page: 2, pageSize: 10));

        var result = await Controller().Index(page: 2);

        var model = Assert.IsType<FavoritesIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(2, model.Resources.Count);
        Assert.Equal(2, model.Page);
        Assert.Equal(2, model.TotalPages);
        Assert.Equal(12, model.TotalCount);
    }

    [Fact]
    public async Task Add_SavesForTheSignedInUser_AndGoesBackToTheSamePage()
    {
        _service.Setup(s => s.AddAsync("u1", 5)).ReturnsAsync(ServiceResult.Success());

        var result = await Controller().Add(5, "/Resources?categoryId=3&page=2");

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Resources?categoryId=3&page=2", redirect.Url);
        _service.Verify(s => s.AddAsync("u1", 5), Times.Once);
    }

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Add_AnAddressOutsideThisSite_IsNeverFollowed(string? returnUrl)
    {
        _service.Setup(s => s.AddAsync("u1", 5)).ReturnsAsync(ServiceResult.Success());

        var result = await Controller().Add(5, returnUrl);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public async Task Add_UnknownResource_ReturnsNotFound()
    {
        _service.Setup(s => s.AddAsync("u1", 99)).ReturnsAsync(ServiceResult.Failure(ServiceError.NotFound, "Ресурсът не е намерен."));

        var result = await Controller().Add(99, "/Resources");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Remove_RemovesForTheSignedInUser_AndGoesBack()
    {
        _service.Setup(s => s.RemoveAsync("u1", 5)).ReturnsAsync(ServiceResult.Success());

        var result = await Controller().Remove(5, "/Favorites?page=2");

        Assert.Equal("/Favorites?page=2", Assert.IsType<LocalRedirectResult>(result).Url);
        _service.Verify(s => s.RemoveAsync("u1", 5), Times.Once);
    }

    [Fact]
    public async Task Remove_AnExternalAddress_GoesToTheFavorites()
    {
        _service.Setup(s => s.RemoveAsync("u1", 5)).ReturnsAsync(ServiceResult.Success());

        var result = await Controller().Remove(5, "https://evil.example");

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }
}

public class CommentsControllerTests
{
    private readonly Mock<ICommentService> _service = new();

    private CommentsController Controller(string userId = "u1", params string[] roles) =>
        new CommentsController(_service.Object).WithUser(userId, roles);

    private static Comment Saved(int resourceId) => new() { Id = 1, ResourceId = resourceId };

    // ---------- Add ----------

    [Fact]
    public async Task Add_Success_ReturnsToTheFirstPageOfCommentsOfTheResource()
    {
        _service.Setup(s => s.AddAsync(7, "u1", "Здравейте"))
            .ReturnsAsync(ServiceResult<Comment>.Success(Saved(7)));
        var controller = Controller();

        var result = await controller.Add(7, "Здравейте");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("Resources", redirect.ControllerName);
        Assert.Equal(7, redirect.RouteValues!["id"]);
        Assert.Equal(1, redirect.RouteValues["commentPage"]);
        Assert.Equal("comments", redirect.Fragment);
        Assert.Null(controller.TempDataText("CommentError"));
    }

    [Fact]
    public async Task Add_Refused_KeepsTheMessageAndTheTypedTextForThePage()
    {
        _service.Setup(s => s.AddAsync(7, "u1", "   "))
            .ReturnsAsync(ServiceResult<Comment>.Failure(ServiceError.Invalid, "Напишете коментар."));
        var controller = Controller();

        var result = await controller.Add(7, "   ");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Напишете коментар.", controller.TempDataText("CommentError"));
        Assert.Equal("   ", controller.TempDataText("CommentDraft"));
    }

    [Fact]
    public async Task Add_WithoutText_IsSentToTheServiceAsEmpty()
    {
        _service.Setup(s => s.AddAsync(7, "u1", string.Empty))
            .ReturnsAsync(ServiceResult<Comment>.Failure(ServiceError.Invalid, "Напишете коментар."));

        await Controller().Add(7, null);

        _service.Verify(s => s.AddAsync(7, "u1", string.Empty), Times.Once);
    }

    [Fact]
    public async Task Add_UnknownResource_ReturnsNotFound()
    {
        _service.Setup(s => s.AddAsync(99, "u1", "текст"))
            .ReturnsAsync(ServiceResult<Comment>.Failure(ServiceError.NotFound, "Ресурсът не е намерен."));

        Assert.IsType<NotFoundResult>(await Controller().Add(99, "текст"));
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_ByTheAuthor_ReturnsToTheSamePageOfComments()
    {
        _service.Setup(s => s.DeleteAsync(4, "u1", false)).ReturnsAsync(ServiceResult<Comment>.Success(Saved(7)));

        var result = await Controller().Delete(4, commentPage: 3);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(7, redirect.RouteValues!["id"]);
        Assert.Equal(3, redirect.RouteValues["commentPage"]);
        Assert.Equal("comments", redirect.Fragment);
    }

    [Fact]
    public async Task Delete_ByAnAdministrator_IsAskedAsAnAdministrator()
    {
        _service.Setup(s => s.DeleteAsync(4, "admin", true)).ReturnsAsync(ServiceResult<Comment>.Success(Saved(7)));

        await Controller("admin", Roles.Administrator).Delete(4);

        _service.Verify(s => s.DeleteAsync(4, "admin", true), Times.Once);
    }

    [Fact]
    public async Task Delete_ByAnotherUser_IsForbidden()
    {
        _service.Setup(s => s.DeleteAsync(4, "u1", false))
            .ReturnsAsync(ServiceResult<Comment>.Failure(ServiceError.Forbidden, "Нямате права."));

        Assert.IsType<ForbidResult>(await Controller().Delete(4));
    }

    [Fact]
    public async Task Delete_UnknownComment_ReturnsNotFound()
    {
        _service.Setup(s => s.DeleteAsync(99, "u1", false))
            .ReturnsAsync(ServiceResult<Comment>.Failure(ServiceError.NotFound, "Коментарът не е намерен."));

        Assert.IsType<NotFoundResult>(await Controller().Delete(99));
    }
}
