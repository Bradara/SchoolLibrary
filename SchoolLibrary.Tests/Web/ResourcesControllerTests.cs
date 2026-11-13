using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Controllers;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Favorites;
using SchoolLibrary.Services.GradeLevels;
using SchoolLibrary.Services.Licenses;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

public class ResourcesControllerTests
{
    private readonly Mock<IResourceService> _resources = new();
    private readonly Mock<ICategoryService> _categories = new();
    private readonly Mock<IGradeLevelService> _gradeLevels = new();
    private readonly Mock<ILicenseService> _licenses = new();
    private readonly Mock<IFavoriteService> _favorites = new();
    private readonly Mock<ICommentService> _comments = new();

    private ResourceFilter? _filter;

    public ResourcesControllerTests()
    {
        _resources.Setup(r => r.SearchAsync(It.IsAny<ResourceFilter>()))
            .Callback<ResourceFilter>(f => _filter = f)
            .ReturnsAsync(new PagedResult<Resource>(
                new List<Resource> { new() { Id = 1, Title = "А" }, new() { Id = 2, Title = "Б" } }, 2, 1, 10));
        _resources.Setup(r => r.GetOwnersAsync()).ReturnsAsync(new List<ResourceOwnerOption>());
        _categories.Setup(c => c.GetOptionsAsync()).ReturnsAsync(new List<Category> { new() { Id = 1, Name = "Математика" } });
        _gradeLevels.Setup(g => g.GetAllAsync()).ReturnsAsync(new List<GradeLevel> { new() { Id = 1, Number = 5 } });
        _licenses.Setup(l => l.GetOptionsAsync()).ReturnsAsync(new List<License> { new() { Id = 1, Name = "CC BY 4.0" } });
        _comments.Setup(c => c.GetForResourceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new PagedResult<Comment>(new List<Comment>(), 0, 1, 10));
    }

    private ResourcesController Controller(string? userId = "u1", params string[] roles) =>
        new ResourcesController(_resources.Object, _categories.Object, _gradeLevels.Object, _licenses.Object,
            _favorites.Object, _comments.Object).WithUser(userId, roles);

    private static ResourceFormViewModel ValidForm() => new()
    {
        Title = "Дроби",
        Author = "Автор",
        Url = "https://example.com/a",
        LicenseId = 1,
        Kind = ResourceKind.External,
        SelectedCategoryIds = { 1 },
        SelectedGradeLevelIds = { 1 }
    };

    private static ServiceResult<Resource> Saved(int id = 5) =>
        ServiceResult<Resource>.Success(new Resource { Id = id, Title = "Дроби", Url = "https://example.com/a" });

    private static ServiceResult<Resource> Failed(ServiceError error, string message = "грешка") =>
        ServiceResult<Resource>.Failure(error, message);

    // ---------- the list ----------

    [Fact]
    public async Task Index_PassesTheFiltersToTheService()
    {
        await Controller().Index(ResourceKind.External, "дроби", 3, 4, "owner", new DateTime(2026, 1, 1), new DateTime(2026, 2, 1),
            ResourceSortOrder.TitleAsc, page: 2);

        Assert.Equal(ResourceKind.External, _filter!.Kind);
        Assert.Equal("дроби", _filter.SearchText);
        Assert.Equal(3, _filter.CategoryId);
        Assert.Equal(4, _filter.GradeLevelId);
        Assert.Equal("owner", _filter.OwnerId);
        Assert.Equal(new DateTime(2026, 1, 1), _filter.FromDate);
        Assert.Equal(new DateTime(2026, 2, 1), _filter.ToDate);
        Assert.Equal(ResourceSortOrder.TitleAsc, _filter.SortOrder);
        Assert.Equal(2, _filter.Page);
    }

    [Fact]
    public async Task Index_WithoutASortOrder_ShowsTheNewestFirst()
    {
        var result = await Controller().Index(null, null, null, null, null, null, null, null);

        Assert.Equal(ResourceSortOrder.DateDesc, _filter!.SortOrder);
        Assert.Equal(ResourceSortOrder.DateDesc, Assert.IsType<ResourceIndexViewModel>(Assert.IsType<ViewResult>(result).Model).SortOrder);
    }

    [Fact]
    public async Task Index_ShowsThePageAndTheOptionsOfTheFilters()
    {
        var result = await Controller().Index(ResourceKind.Lesson, null, null, null, null, null, null, null);

        var model = Assert.IsType<ResourceIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(ResourceKind.Lesson, model.Kind);
        Assert.Equal(2, model.Resources.Count);
        Assert.Equal(2, model.TotalCount);
        Assert.Single(model.Categories);
        Assert.Single(model.GradeLevels);
    }

    [Fact]
    public async Task Index_ForAVisitor_DoesNotLookUpFavorites()
    {
        var result = await Controller(userId: null).Index(null, null, null, null, null, null, null, null);

        var model = Assert.IsType<ResourceIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Empty(model.FavoriteIds);
        _favorites.Verify(f => f.GetFavoriteIdsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<int>>()), Times.Never);
    }

    [Fact]
    public async Task Index_ForASignedInUser_MarksTheResourcesOfThePageThatAreFavorites()
    {
        _favorites.Setup(f => f.GetFavoriteIdsAsync("u1", It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 1, 2 }))))
            .ReturnsAsync(new HashSet<int> { 2 });

        var result = await Controller().Index(null, null, null, null, null, null, null, null);

        var model = Assert.IsType<ResourceIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(new[] { 2 }, model.FavoriteIds);
    }

    // ---------- the details ----------

    [Fact]
    public async Task Details_WithoutAnId_ReturnsNotFound()
    {
        Assert.IsType<NotFoundResult>(await Controller().Details(null));
    }

    [Fact]
    public async Task Details_UnknownResource_ReturnsNotFound()
    {
        Assert.IsType<NotFoundResult>(await Controller().Details(99));
    }

    [Fact]
    public async Task Details_ShowsTheResourceWithItsFavoritesAndCommentsPage()
    {
        _resources.Setup(r => r.GetDetailsAsync(5)).ReturnsAsync(new Resource { Id = 5, Title = "Дроби" });
        _favorites.Setup(f => f.IsFavoriteAsync("u1", 5)).ReturnsAsync(true);
        _favorites.Setup(f => f.CountAsync(5)).ReturnsAsync(7);
        _comments.Setup(c => c.GetForResourceAsync(5, 3, It.IsAny<int>()))
            .ReturnsAsync(new PagedResult<Comment>(new List<Comment> { new() { Text = "текст" } }, 25, 3, 10));

        var result = await Controller().Details(5, commentPage: 3);

        var model = Assert.IsType<ResourceDetailsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Дроби", model.Resource.Title);
        Assert.True(model.IsFavorite);
        Assert.Equal(7, model.FavoriteCount);
        Assert.Single(model.Comments);
        Assert.Equal(3, model.CommentPage);
        Assert.Equal(3, model.CommentTotalPages);
        Assert.Equal(25, model.CommentTotal);
    }

    [Fact]
    public async Task Details_ForAVisitor_IsNotAFavoriteAndNobodyIsAsked()
    {
        _resources.Setup(r => r.GetDetailsAsync(5)).ReturnsAsync(new Resource { Id = 5, Title = "Дроби" });

        var result = await Controller(userId: null).Details(5);

        Assert.False(Assert.IsType<ResourceDetailsViewModel>(Assert.IsType<ViewResult>(result).Model).IsFavorite);
        _favorites.Verify(f => f.IsFavoriteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Details_BringsBackTheRefusedCommentAndItsMessage()
    {
        _resources.Setup(r => r.GetDetailsAsync(5)).ReturnsAsync(new Resource { Id = 5, Title = "Дроби" });
        var controller = Controller();
        controller.TempData["CommentError"] = "Напишете коментар.";
        controller.TempData["CommentDraft"] = "недовършен текст";

        var result = await controller.Details(5);

        var model = Assert.IsType<ResourceDetailsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Напишете коментар.", model.CommentError);
        Assert.Equal("недовършен текст", model.CommentDraft);
    }

    // ---------- adding ----------

    [Fact]
    public async Task Create_Get_StartsInTheSectionTheUserCameFrom()
    {
        var result = await Controller().Create(ResourceKind.External);

        var model = Assert.IsType<ResourceFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(ResourceKind.External, model.Kind);
        Assert.Single(model.AllCategories);
        Assert.Single(model.AllGradeLevels);
        Assert.Single(model.AllLicenses);
    }

    [Fact]
    public async Task Create_Post_WithoutSubjectsAndGrades_ShowsTheErrorsNextToTheChoices_AndSavesNothing()
    {
        var controller = Controller();
        var form = ValidForm();
        form.SelectedCategoryIds.Clear();
        form.SelectedGradeLevelIds.Clear();

        var result = await controller.Create(form);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey(nameof(ResourceFormViewModel.SelectedCategoryIds)));
        Assert.True(controller.ModelState.ContainsKey(nameof(ResourceFormViewModel.SelectedGradeLevelIds)));
        _resources.Verify(r => r.CreateAsync(It.IsAny<ResourceInput>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Create_Post_SendsTheFormToTheService_AsTheSignedInUser()
    {
        ResourceInput? sent = null;
        _resources.Setup(r => r.CreateAsync(It.IsAny<ResourceInput>(), "u1"))
            .Callback<ResourceInput, string?>((i, _) => sent = i)
            .ReturnsAsync(Saved());
        var controller = Controller();

        var result = await controller.Create(ValidForm());

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("Дроби", controller.TempDataText("SuccessMessage"));
        Assert.Equal("Дроби", sent!.Title);
        Assert.Equal("Автор", sent.Author);
        Assert.Equal("https://example.com/a", sent.Url);
        Assert.Equal(1, sent.LicenseId);
        Assert.Equal(ResourceKind.External, sent.Kind);
        Assert.Equal(new[] { 1 }, sent.CategoryIds);
        Assert.Equal(new[] { 1 }, sent.GradeLevelIds);
    }

    [Fact]
    public async Task Create_Post_ALinkThatIsAlreadyInTheLibrary_OffersAnAddressOfTheExistingResource()
    {
        _resources.Setup(r => r.CreateAsync(It.IsAny<ResourceInput>(), "u1")).ReturnsAsync(Failed(ServiceError.Conflict, "Вече съществува."));
        _resources.Setup(r => r.FindByUrlAsync("https://example.com/a")).ReturnsAsync(new Resource { Id = 42 });
        var controller = Controller();

        var result = await controller.Create(ValidForm());

        var model = Assert.IsType<ResourceFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(42, model.DuplicateOfId);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, e => e.ErrorMessage == "Вече съществува.");
        Assert.Null(controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Create_Post_ARefusedForm_ShowsTheMessageAndKeepsTheOptions()
    {
        _resources.Setup(r => r.CreateAsync(It.IsAny<ResourceInput>(), "u1")).ReturnsAsync(Failed(ServiceError.Invalid, "Невалиден адрес."));
        var controller = Controller();

        var result = await controller.Create(ValidForm());

        var model = Assert.IsType<ResourceFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Null(model.DuplicateOfId);
        Assert.Single(model.AllCategories);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, e => e.ErrorMessage == "Невалиден адрес.");
    }

    // ---------- editing ----------

    [Fact]
    public async Task Edit_Get_FillsTheFormWithTheResource()
    {
        var resource = new Resource
        {
            Id = 5, Title = "Дроби", Author = "А", Url = "https://example.com/a", Kind = ResourceKind.External, LicenseId = 1,
            Categories = { new Category { Id = 1 } }, GradeLevels = { new GradeLevel { Id = 1 } }
        };
        _resources.Setup(r => r.GetForEditAsync(5, "u1", false)).ReturnsAsync(ServiceResult<Resource>.Success(resource));

        var result = await Controller().Edit(5);

        var form = Assert.IsType<ResourceFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(5, form.Id);
        Assert.Equal("Дроби", form.Title);
        Assert.Equal(ResourceKind.External, form.Kind);
        Assert.Equal(1, form.LicenseId);
        Assert.Equal(new[] { 1 }, form.SelectedCategoryIds);
        Assert.Equal(new[] { 1 }, form.SelectedGradeLevelIds);
    }

    [Fact]
    public async Task Edit_Get_WithoutAnId_ReturnsNotFound()
    {
        Assert.IsType<NotFoundResult>(await Controller().Edit((int?)null));
    }

    [Theory]
    [InlineData(ServiceError.Forbidden, typeof(ForbidResult))]
    [InlineData(ServiceError.NotFound, typeof(NotFoundResult))]
    public async Task Edit_Get_RefusedByTheService_BecomesTheMatchingResponse(ServiceError error, Type expected)
    {
        _resources.Setup(r => r.GetForEditAsync(5, "u1", false)).ReturnsAsync(Failed(error));

        Assert.IsType(expected, await Controller().Edit(5));
    }

    [Fact]
    public async Task Edit_Get_AsAnAdministrator_AsksAsAnAdministrator()
    {
        _resources.Setup(r => r.GetForEditAsync(5, "admin", true)).ReturnsAsync(Saved());

        await Controller("admin", Roles.Administrator).Edit(5);

        _resources.Verify(r => r.GetForEditAsync(5, "admin", true), Times.Once);
    }

    [Fact]
    public async Task Edit_Post_AnotherId_ReturnsNotFound_WithoutSaving()
    {
        var form = ValidForm();
        form.Id = 6;

        Assert.IsType<NotFoundResult>(await Controller().Edit(5, form));
        _resources.Verify(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<ResourceInput>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Edit_Post_Saves_AndGoesToTheList()
    {
        var form = ValidForm();
        form.Id = 5;
        _resources.Setup(r => r.UpdateAsync(5, It.IsAny<ResourceInput>(), "u1", false)).ReturnsAsync(Saved());
        var controller = Controller();

        var result = await controller.Edit(5, form);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("редактиран", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Edit_Post_AsAnAdministrator_UpdatesAsAnAdministrator()
    {
        var form = ValidForm();
        form.Id = 5;
        _resources.Setup(r => r.UpdateAsync(5, It.IsAny<ResourceInput>(), "admin", true)).ReturnsAsync(Saved());

        await Controller("admin", Roles.Administrator).Edit(5, form);

        _resources.Verify(r => r.UpdateAsync(5, It.IsAny<ResourceInput>(), "admin", true), Times.Once);
    }

    [Theory]
    [InlineData(ServiceError.Forbidden, typeof(ForbidResult))]
    [InlineData(ServiceError.NotFound, typeof(NotFoundResult))]
    public async Task Edit_Post_RefusedByTheService_BecomesTheMatchingResponse(ServiceError error, Type expected)
    {
        var form = ValidForm();
        form.Id = 5;
        _resources.Setup(r => r.UpdateAsync(5, It.IsAny<ResourceInput>(), "u1", false)).ReturnsAsync(Failed(error));

        Assert.IsType(expected, await Controller().Edit(5, form));
    }

    [Theory]
    [InlineData(ServiceError.Invalid)]
    [InlineData(ServiceError.Conflict)]
    public async Task Edit_Post_AFormProblem_ShowsTheFormAgainWithTheMessage(ServiceError error)
    {
        var form = ValidForm();
        form.Id = 5;
        _resources.Setup(r => r.UpdateAsync(5, It.IsAny<ResourceInput>(), "u1", false)).ReturnsAsync(Failed(error, "Проблем с формата."));
        _resources.Setup(r => r.FindByUrlAsync(It.IsAny<string>())).ReturnsAsync(new Resource { Id = 42 });
        var controller = Controller();

        var result = await controller.Edit(5, form);

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, e => e.ErrorMessage == "Проблем с формата.");
    }

    // ---------- deleting ----------

    [Fact]
    public async Task Delete_Get_WithoutAnId_ReturnsNotFound()
    {
        Assert.IsType<NotFoundResult>(await Controller().Delete(null));
    }

    [Fact]
    public async Task Delete_Get_ShowsTheConfirmationOfAResourceTheUserMayDelete()
    {
        var resource = new Resource { Id = 5, Title = "Дроби" };
        _resources.Setup(r => r.GetForDeleteAsync(5, "u1", false)).ReturnsAsync(ServiceResult<Resource>.Success(resource));

        var result = await Controller().Delete(5);

        Assert.Same(resource, Assert.IsType<ViewResult>(result).Model);
    }

    [Fact]
    public async Task Delete_Get_AnotherUsersResource_IsForbidden()
    {
        _resources.Setup(r => r.GetForDeleteAsync(5, "u1", false)).ReturnsAsync(Failed(ServiceError.Forbidden));

        Assert.IsType<ForbidResult>(await Controller().Delete(5));
    }

    [Fact]
    public async Task DeleteConfirmed_Deletes_AndSaysSo()
    {
        _resources.Setup(r => r.DeleteAsync(5, "u1", false)).ReturnsAsync(Saved());
        var controller = Controller();

        var result = await controller.DeleteConfirmed(5);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("изтрит", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task DeleteConfirmed_AsAnAdministrator_DeletesAsAnAdministrator()
    {
        _resources.Setup(r => r.DeleteAsync(5, "admin", true)).ReturnsAsync(Saved());

        await Controller("admin", Roles.Administrator).DeleteConfirmed(5);

        _resources.Verify(r => r.DeleteAsync(5, "admin", true), Times.Once);
    }

    [Theory]
    [InlineData(ServiceError.Forbidden, typeof(ForbidResult))]
    [InlineData(ServiceError.NotFound, typeof(NotFoundResult))]
    [InlineData(ServiceError.Invalid, typeof(BadRequestObjectResult))]
    public async Task DeleteConfirmed_Refused_BecomesTheMatchingResponse(ServiceError error, Type expected)
    {
        _resources.Setup(r => r.DeleteAsync(5, "u1", false)).ReturnsAsync(Failed(error));

        Assert.IsType(expected, await Controller().DeleteConfirmed(5));
    }
}
