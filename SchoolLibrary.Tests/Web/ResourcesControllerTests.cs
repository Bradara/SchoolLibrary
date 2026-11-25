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
using SchoolLibrary.Services.Markdown;
using SchoolLibrary.Services.Reading;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.Services.Storage;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

public class ResourcesControllerTests
{
    private readonly Mock<IResourceService> _resources = new();
    private readonly Mock<IResourceUploadService> _uploads = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IMarkdownRenderer> _markdown = new();
    private readonly Mock<ICategoryService> _categories = new();
    private readonly Mock<IGradeLevelService> _gradeLevels = new();
    private readonly Mock<ILicenseService> _licenses = new();
    private readonly Mock<IFavoriteService> _favorites = new();
    private readonly Mock<ICommentService> _comments = new();
    private readonly Mock<IReadingPositionService> _reading = new();
    private readonly Mock<IBookmarkService> _bookmarks = new();
    private readonly Mock<IHighlightService> _highlights = new();

    private ResourceFilter? _filter;
    private ResourceFilter? _facetFilter;

    private static readonly CatalogFacets Facets = new(
        new Dictionary<ResourceKind, int> { [ResourceKind.Lesson] = 2 },
        new[] { new SubjectFacet(1, "Математика", "bi-plus-slash-minus", "#2f6fed", 2) });

    public ResourcesControllerTests()
    {
        _resources.Setup(r => r.SearchAsync(It.IsAny<ResourceFilter>()))
            .Callback<ResourceFilter>(f => _filter = f)
            .ReturnsAsync(new PagedResult<Resource>(
                new List<Resource> { new() { Id = 1, Title = "А" }, new() { Id = 2, Title = "Б" } }, 2, 1, 10));
        _resources.Setup(r => r.GetOwnersAsync(It.IsAny<ResourceViewer>())).ReturnsAsync(new List<ResourceOwnerOption>());
        _resources.Setup(r => r.GetCatalogFacetsAsync(It.IsAny<ResourceFilter>()))
            .Callback<ResourceFilter>(f => _facetFilter = f)
            .ReturnsAsync(Facets);
        _categories.Setup(c => c.GetOptionsAsync()).ReturnsAsync(new List<Category> { new() { Id = 1, Name = "Математика" } });
        _gradeLevels.Setup(g => g.GetAllAsync()).ReturnsAsync(new List<GradeLevel> { new() { Id = 1, Number = 5 } });
        _licenses.Setup(l => l.GetOptionsAsync()).ReturnsAsync(new List<License> { new() { Id = 1, Name = "CC BY 4.0" } });
        _comments.Setup(c => c.GetForResourceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new PagedResult<Comment>(new List<Comment>(), 0, 1, 10));
    }

    private ResourcesController Controller(string? userId = "u1", params string[] roles) =>
        new ResourcesController(_resources.Object, _uploads.Object, _storage.Object, _markdown.Object,
            _categories.Object, _gradeLevels.Object, _licenses.Object, _favorites.Object, _comments.Object, _reading.Object, _bookmarks.Object, _highlights.Object)
            .WithUser(userId, roles);

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

    // ---------- Download ----------

    private static ResourceFile PdfFile() => new()
    {
        ResourceId = 5,
        StorageKey = "resources/x.pdf",
        ContentType = "application/pdf",
        FileName = "материал.pdf"
    };

    [Fact]
    public async Task Download_ReturnsTheFile_WhenItExists()
    {
        _resources.Setup(r => r.GetFileAsync(5, It.IsAny<ResourceViewer>())).ReturnsAsync(PdfFile());
        _storage.Setup(s => s.OpenReadAsync("resources/x.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(new byte[] { 1, 2, 3 }));

        var result = await Controller().Download(5);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
    }

    [Fact]
    public async Task Download_ReturnsNotFound_WhenResourceHasNoFile()
    {
        _resources.Setup(r => r.GetFileAsync(It.IsAny<int>(), It.IsAny<ResourceViewer>())).ReturnsAsync((ResourceFile?)null);

        Assert.IsType<NotFoundResult>(await Controller().Download(5));
    }

    [Fact]
    public async Task Download_ReturnsNotFound_WhenTheBytesAreMissing()
    {
        _resources.Setup(r => r.GetFileAsync(5, It.IsAny<ResourceViewer>())).ReturnsAsync(PdfFile());
        _storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        Assert.IsType<NotFoundResult>(await Controller().Download(5));
    }

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
    public async Task Index_NothingChosen_ShowsTheTiles_AndOnlyTheNewestFew()
    {
        var result = await Controller().Index(ResourceKind.Lesson, null, null, 4, null, null, null,
            ResourceSortOrder.TitleAsc, page: 3);

        var model = Assert.IsType<ResourceIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.Query.ShowsTiles);
        // The newest few, whatever the page and the order in the address
        Assert.Equal(1, _filter!.Page);
        Assert.Equal(ResourceIndexViewModel.NewestOnTiles, _filter.PageSize);
        Assert.Equal(ResourceSortOrder.DateDesc, _filter.SortOrder);
        // The kind and the grade still narrow them; the order stays for the links
        Assert.Equal(ResourceKind.Lesson, _filter.Kind);
        Assert.Equal(4, _filter.GradeLevelId);
        Assert.Equal(ResourceSortOrder.TitleAsc, model.SortOrder);
    }

    [Theory]
    [InlineData("дроби", null, null, false)]
    [InlineData(null, 3, null, false)]
    [InlineData(null, null, "owner", false)]
    [InlineData(null, null, null, true)]
    public async Task Index_ASearchSubjectOwnerOrAll_ShowsTheResultsPage(string? search, int? categoryId, string? ownerId, bool all)
    {
        var result = await Controller().Index(null, search, categoryId, null, ownerId, null, null,
            ResourceSortOrder.TitleAsc, all, page: 2);

        var model = Assert.IsType<ResourceIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(model.Query.ShowsTiles);
        Assert.Equal(all, model.All);
        Assert.Equal(2, _filter!.Page);
        Assert.Equal(ResourceFilter.DefaultPageSize, _filter.PageSize);
        Assert.Equal(ResourceSortOrder.TitleAsc, _filter.SortOrder);
    }

    [Fact]
    public async Task Index_ShowsTheCountsForTheSameFilters()
    {
        var result = await Controller("u1", Roles.Teacher).Index(ResourceKind.External, "дроби", 3, 4, null, null, null, null);

        var model = Assert.IsType<ResourceIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Same(Facets, model.Facets);
        Assert.Same(_filter, _facetFilter);
        Assert.Equal("u1", _facetFilter!.Viewer.UserId);
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
        _resources.Setup(r => r.GetDetailsAsync(5, It.IsAny<ResourceViewer>())).ReturnsAsync(new Resource { Id = 5, Title = "Дроби" });
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
        _resources.Setup(r => r.GetDetailsAsync(5, It.IsAny<ResourceViewer>())).ReturnsAsync(new Resource { Id = 5, Title = "Дроби" });

        var result = await Controller(userId: null).Details(5);

        Assert.False(Assert.IsType<ResourceDetailsViewModel>(Assert.IsType<ViewResult>(result).Model).IsFavorite);
        _favorites.Verify(f => f.IsFavoriteAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Details_BringsBackTheRefusedCommentAndItsMessage()
    {
        _resources.Setup(r => r.GetDetailsAsync(5, It.IsAny<ResourceViewer>())).ReturnsAsync(new Resource { Id = 5, Title = "Дроби" });
        var controller = Controller();
        controller.TempData["CommentError"] = "Напишете коментар.";
        controller.TempData["CommentDraft"] = "недовършен текст";

        var result = await controller.Details(5);

        var model = Assert.IsType<ResourceDetailsViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Напишете коментар.", model.CommentError);
        Assert.Equal("недовършен текст", model.CommentDraft);
    }

    // ---------- visibility ----------

    [Fact]
    public async Task Index_ListsWhatTheSignedInUserMaySee()
    {
        await Controller("u1", Roles.Teacher).Index(null, null, null, null, null, null, null, null);

        Assert.Equal("u1", _filter!.Viewer.UserId);
        Assert.Equal(ResourceAudience.Guests | ResourceAudience.Teachers, _filter.Viewer.Audience);
    }

    [Fact]
    public async Task Index_ForAVisitor_ListsOnlyPublicResources()
    {
        await Controller(userId: null).Index(null, null, null, null, null, null, null, null);

        Assert.Equal(ResourceViewer.Anonymous, _filter!.Viewer);
    }

    [Fact]
    public async Task Details_ResourceHiddenFromTheUser_ReturnsNotFound()
    {
        _resources.Setup(r => r.GetDetailsAsync(5, It.Is<ResourceViewer>(v => v.Audience.HasFlag(ResourceAudience.Teachers))))
            .ReturnsAsync(new Resource { Id = 5, Title = "Само за учители" });

        Assert.IsType<NotFoundResult>(await Controller("u1", Roles.Student).Details(5));
        Assert.IsType<ViewResult>(await Controller("u1", Roles.Teacher).Details(5));
    }

    [Theory]
    [InlineData("u1", false, true)]
    [InlineData("u2", false, false)]
    [InlineData("u2", true, false)]
    public async Task Details_OnlyTheOwnerMayChangeTheAudience(string userId, bool admin, bool expected)
    {
        _resources.Setup(r => r.GetDetailsAsync(5, It.IsAny<ResourceViewer>()))
            .ReturnsAsync(new Resource { Id = 5, Title = "Дроби", OwnerId = "u1" });

        var controller = admin ? Controller(userId, Roles.Administrator) : Controller(userId);
        var model = Assert.IsType<ResourceDetailsViewModel>(Assert.IsType<ViewResult>(await controller.Details(5)).Model);

        Assert.Equal(expected, model.CanChangeAudience);
    }

    [Fact]
    public async Task SetAudience_CombinesTheTickedGroups_AndGoesBackToTheDetails()
    {
        _resources.Setup(r => r.SetAudienceAsync(5, ResourceAudience.Students | ResourceAudience.Teachers, "u1", false))
            .ReturnsAsync(ServiceResult<Resource>.Success(new Resource { Id = 5, Audience = ResourceAudience.Students | ResourceAudience.Teachers }));
        var controller = Controller();

        var result = await controller.SetAudience(5, new List<ResourceAudience>
        {
            ResourceAudience.None, ResourceAudience.Students, ResourceAudience.Teachers
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(5, redirect.RouteValues!["id"]);
        Assert.Equal("Видимост: Ученици, Учители.", controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task SetAudience_NothingTicked_MakesItPrivate()
    {
        _resources.Setup(r => r.SetAudienceAsync(5, ResourceAudience.None, "u1", false))
            .ReturnsAsync(ServiceResult<Resource>.Success(new Resource { Id = 5, Audience = ResourceAudience.None }));
        var controller = Controller();

        await controller.SetAudience(5, null);

        Assert.Equal("Видимост: Само аз.", controller.TempDataText("SuccessMessage"));
    }

    [Theory]
    [InlineData(ServiceError.Forbidden, typeof(ForbidResult))]
    [InlineData(ServiceError.NotFound, typeof(NotFoundResult))]
    public async Task SetAudience_RefusedByTheService_BecomesTheMatchingResponse(ServiceError error, Type expected)
    {
        _resources.Setup(r => r.SetAudienceAsync(5, It.IsAny<ResourceAudience>(), "u1", false))
            .ReturnsAsync(ServiceResult<Resource>.Failure(error, "не"));

        Assert.IsType(expected, await Controller().SetAudience(5, null));
    }

    [Fact]
    public async Task Create_Post_SendsTheChosenAudience()
    {
        ResourceInput? sent = null;
        _resources.Setup(r => r.CreateAsync(It.IsAny<ResourceInput>(), "u1"))
            .Callback<ResourceInput, string?>((i, _) => sent = i)
            .ReturnsAsync(Saved());
        var form = ValidForm();
        form.Audience = new List<ResourceAudience> { ResourceAudience.None, ResourceAudience.Teachers };

        await Controller().Create(form);

        Assert.Equal(ResourceAudience.Teachers, sent!.Audience);
    }

    [Fact]
    public void NewForms_StartVisibleToEveryone()
    {
        Assert.Equal(ResourceAudience.Everyone, ResourceVisibility.Combine(new ResourceFormViewModel().Audience));
        Assert.Equal(ResourceAudience.Everyone, ResourceVisibility.Combine(new ResourceUploadViewModel().Audience));
    }

    [Fact]
    public async Task Edit_Get_TicksTheSavedAudience()
    {
        var resource = new Resource { Id = 5, Title = "Дроби", Audience = ResourceAudience.Parents | ResourceAudience.Teachers };
        _resources.Setup(r => r.GetForEditAsync(5, "u1", false)).ReturnsAsync(ServiceResult<Resource>.Success(resource));

        var form = Assert.IsType<ResourceFormViewModel>(Assert.IsType<ViewResult>(await Controller().Edit(5)).Model);

        Assert.Equal(new[] { ResourceAudience.Parents, ResourceAudience.Teachers }, form.Audience);
    }

    // ---------- the reading position of a book ----------

    private static Resource Book() => new()
    {
        Id = 5,
        Title = "Под игото",
        Kind = ResourceKind.Upload,
        File = new ResourceFile { ResourceId = 5, ContentType = ReadingPositionService.EpubContentType }
    };

    [Fact]
    public async Task Details_OfABook_BringsTheSavedReadingPosition()
    {
        _resources.Setup(r => r.GetDetailsAsync(5, It.IsAny<ResourceViewer>())).ReturnsAsync(Book());
        var saved = new ReadingPositionInfo("epubcfi(/6/4!/4/2/1:0)", 40, DateTime.UtcNow);
        _reading.Setup(r => r.GetAsync("u1", 5)).ReturnsAsync(saved);

        var result = await Controller().Details(5);

        Assert.Same(saved, Assert.IsType<ResourceDetailsViewModel>(Assert.IsType<ViewResult>(result).Model).ReadingPosition);
    }

    [Fact]
    public async Task Details_ForAVisitor_DoesNotLookForAReadingPosition()
    {
        _resources.Setup(r => r.GetDetailsAsync(5, It.IsAny<ResourceViewer>())).ReturnsAsync(Book());

        var result = await Controller(userId: null).Details(5);

        Assert.Null(Assert.IsType<ResourceDetailsViewModel>(Assert.IsType<ViewResult>(result).Model).ReadingPosition);
        _reading.Verify(r => r.GetAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Details_OfAResourceThatIsNotABook_DoesNotLookForAReadingPosition()
    {
        _resources.Setup(r => r.GetDetailsAsync(5, It.IsAny<ResourceViewer>())).ReturnsAsync(new Resource { Id = 5, Title = "Дроби" });

        await Controller().Details(5);

        _reading.Verify(r => r.GetAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SaveReadingPosition_SavesForTheCurrentUser()
    {
        _reading.Setup(r => r.SaveAsync("u1", 5, "epubcfi(/6/4!/4/2/1:0)", 40)).ReturnsAsync(ServiceResult.Success());

        var result = await Controller().SaveReadingPosition(5, "epubcfi(/6/4!/4/2/1:0)", 40);

        Assert.IsType<JsonResult>(result);
        _reading.Verify(r => r.SaveAsync("u1", 5, "epubcfi(/6/4!/4/2/1:0)", 40), Times.Once);
    }

    [Fact]
    public async Task SaveReadingPosition_UnknownBook_ReturnsNotFound()
    {
        _reading.Setup(r => r.SaveAsync("u1", 9, It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync(ServiceResult.Failure(ServiceError.NotFound, "Книгата не е намерена."));

        Assert.IsType<NotFoundResult>(await Controller().SaveReadingPosition(9, "epubcfi(/6)", 1));
    }

    [Fact]
    public async Task SaveReadingPosition_InvalidPosition_ReturnsBadRequest()
    {
        _reading.Setup(r => r.SaveAsync("u1", 5, It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync(ServiceResult.Failure(ServiceError.Invalid, "Невалидна позиция в книгата."));

        Assert.IsType<BadRequestObjectResult>(await Controller().SaveReadingPosition(5, "x", 1));
    }

    // ---------- bookmarks ----------

    [Fact]
    public async Task Bookmarks_ReturnsTheCurrentUsersBookmarks()
    {
        _bookmarks.Setup(b => b.GetAsync("u1", 5))
            .ReturnsAsync(new List<BookmarkInfo> { new(7, "epubcfi(/6/2)", "Начало", 3) });

        var result = Assert.IsType<JsonResult>(await Controller().Bookmarks(5));

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"id\":7", json);
        Assert.Contains("epubcfi(/6/2)", json);
    }

    [Fact]
    public async Task AddBookmark_AddsForTheCurrentUser()
    {
        _bookmarks.Setup(b => b.AddAsync("u1", 5, "epubcfi(/6/2)", "Начало", 3))
            .ReturnsAsync(ServiceResult<BookmarkInfo>.Success(new BookmarkInfo(7, "epubcfi(/6/2)", "Начало", 3)));

        var result = await Controller().AddBookmark(5, "epubcfi(/6/2)", "Начало", 3);

        Assert.IsType<JsonResult>(result);
        _bookmarks.Verify(b => b.AddAsync("u1", 5, "epubcfi(/6/2)", "Начало", 3), Times.Once);
    }

    [Fact]
    public async Task AddBookmark_UnknownBook_ReturnsNotFound()
    {
        _bookmarks.Setup(b => b.AddAsync("u1", 9, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync(ServiceResult<BookmarkInfo>.Failure(ServiceError.NotFound, "Книгата не е намерена."));

        Assert.IsType<NotFoundResult>(await Controller().AddBookmark(9, "epubcfi(/6)", "x", 1));
    }

    [Fact]
    public async Task AddBookmark_OverTheLimit_ReturnsBadRequestWithTheMessage()
    {
        _bookmarks.Setup(b => b.AddAsync("u1", 5, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync(ServiceResult<BookmarkInfo>.Failure(ServiceError.Conflict, "Твърде много отметки."));

        var result = Assert.IsType<BadRequestObjectResult>(await Controller().AddBookmark(5, "epubcfi(/6)", "x", 1));
        Assert.Equal("Твърде много отметки.", result.Value!.GetType().GetProperty("message")!.GetValue(result.Value));
    }

    [Fact]
    public async Task DeleteBookmark_RemovesTheCurrentUsersBookmark()
    {
        _bookmarks.Setup(b => b.RemoveAsync("u1", 7)).ReturnsAsync(ServiceResult.Success());

        Assert.IsType<JsonResult>(await Controller().DeleteBookmark(7));
        _bookmarks.Verify(b => b.RemoveAsync("u1", 7), Times.Once);
    }

    [Fact]
    public async Task DeleteBookmark_SomeoneElses_ReturnsNotFound()
    {
        _bookmarks.Setup(b => b.RemoveAsync("u1", 7))
            .ReturnsAsync(ServiceResult.Failure(ServiceError.NotFound, "Отметката не е намерена."));

        Assert.IsType<NotFoundResult>(await Controller().DeleteBookmark(7));
    }

    // ---------- highlights and notes ----------

    private static readonly HighlightInfo SavedHighlight = new(4, "epubcfi(/6/2,/1:0,/1:5)", "текст", "green", "бележка", 3);

    private static object? Field(object? value, string name) => value!.GetType().GetProperty(name)!.GetValue(value);

    [Fact]
    public async Task Highlights_ReturnsTheCurrentUsersHighlights()
    {
        _highlights.Setup(h => h.GetAsync("u1", 5)).ReturnsAsync(new List<HighlightInfo> { SavedHighlight });

        var result = Assert.IsType<JsonResult>(await Controller().Highlights(5));

        var item = Assert.Single((IEnumerable<object>)result.Value!);
        Assert.Equal("бележка", Field(item, "note"));
        Assert.Equal("green", Field(item, "color"));
    }

    [Fact]
    public async Task AddHighlight_PassesTheSelectionForTheCurrentUser()
    {
        HighlightInput? sent = null;
        _highlights.Setup(h => h.AddAsync("u1", 5, It.IsAny<HighlightInput>()))
            .Callback<string, int, HighlightInput>((_, _, input) => sent = input)
            .ReturnsAsync(ServiceResult<HighlightInfo>.Success(SavedHighlight));

        var result = Assert.IsType<JsonResult>(await Controller().AddHighlight(5, "epubcfi(/6/2,/1:0,/1:5)", "текст", "green", "бележка", 3));

        Assert.Equal(new HighlightInput("epubcfi(/6/2,/1:0,/1:5)", "текст", "green", "бележка", 3), sent);
        Assert.Equal(4, Field(Field(result.Value, "highlight"), "id"));
    }

    [Fact]
    public async Task AddHighlight_UnknownBook_ReturnsNotFound()
    {
        _highlights.Setup(h => h.AddAsync("u1", 9, It.IsAny<HighlightInput>()))
            .ReturnsAsync(ServiceResult<HighlightInfo>.Failure(ServiceError.NotFound, "Книгата не е намерена."));

        Assert.IsType<NotFoundResult>(await Controller().AddHighlight(9, "epubcfi(/6)", "x", null, null, 0));
    }

    [Fact]
    public async Task UpdateHighlight_Refused_ReturnsBadRequestWithTheMessage()
    {
        _highlights.Setup(h => h.UpdateAsync("u1", 4, "blue", It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<HighlightInfo>.Failure(ServiceError.Invalid, "Бележката е твърде дълга."));

        var result = Assert.IsType<BadRequestObjectResult>(await Controller().UpdateHighlight(4, "blue", "много"));

        Assert.Equal("Бележката е твърде дълга.", Field(result.Value, "message"));
    }

    [Fact]
    public async Task UpdateHighlight_SomeoneElses_ReturnsNotFound()
    {
        _highlights.Setup(h => h.UpdateAsync("u1", 4, It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<HighlightInfo>.Failure(ServiceError.NotFound, "Подчертаването не е намерено."));

        Assert.IsType<NotFoundResult>(await Controller().UpdateHighlight(4, "blue", null));
    }

    [Fact]
    public async Task DeleteHighlight_RemovesForTheCurrentUser()
    {
        _highlights.Setup(h => h.RemoveAsync("u1", 4)).ReturnsAsync(ServiceResult.Success());
        _highlights.Setup(h => h.RemoveAsync("u1", 8))
            .ReturnsAsync(ServiceResult.Failure(ServiceError.NotFound, "Подчертаването не е намерено."));

        Assert.IsType<JsonResult>(await Controller().DeleteHighlight(4));
        Assert.IsType<NotFoundResult>(await Controller().DeleteHighlight(8));
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

    [Fact]
    public async Task DeleteConfirmed_AlsoRemovesTheStoredFile_ForAnUpload()
    {
        _resources.Setup(r => r.GetFileAsync(5, It.IsAny<ResourceViewer>())).ReturnsAsync(PdfFile());
        _resources.Setup(r => r.DeleteAsync(5, "u1", false)).ReturnsAsync(Saved());

        await Controller().DeleteConfirmed(5);

        _storage.Verify(s => s.DeleteAsync("resources/x.pdf", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteConfirmed_AlsoRemovesTheCoverOfABook()
    {
        var book = new ResourceFile
        {
            ResourceId = 5, StorageKey = "resources/b.epub", ContentType = "application/epub+zip",
            CoverKey = "covers/c.jpg", CoverContentType = "image/jpeg"
        };
        _resources.Setup(r => r.GetFileAsync(5, It.IsAny<ResourceViewer>())).ReturnsAsync(book);
        _resources.Setup(r => r.DeleteAsync(5, "u1", false)).ReturnsAsync(Saved());

        await Controller().DeleteConfirmed(5);

        _storage.Verify(s => s.DeleteAsync("resources/b.epub", It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync("covers/c.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteConfirmed_DoesNotTouchStorage_WhenResourceHasNoFile()
    {
        _resources.Setup(r => r.GetFileAsync(5, It.IsAny<ResourceViewer>())).ReturnsAsync((ResourceFile?)null);
        _resources.Setup(r => r.DeleteAsync(5, "u1", false)).ReturnsAsync(Saved());

        await Controller().DeleteConfirmed(5);

        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteConfirmed_WhenRefused_DoesNotRemoveTheFile()
    {
        _resources.Setup(r => r.GetFileAsync(5, It.IsAny<ResourceViewer>())).ReturnsAsync(PdfFile());
        _resources.Setup(r => r.DeleteAsync(5, "u1", false)).ReturnsAsync(Failed(ServiceError.Forbidden));

        await Controller().DeleteConfirmed(5);

        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- uploading ----------

    [Fact]
    public async Task Upload_Get_ReturnsTheFormWithOptions()
    {
        var result = await Controller().Upload();

        var model = Assert.IsType<ResourceUploadViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.NotEmpty(model.AllLicenses);
        Assert.NotEmpty(model.AllCategories);
        Assert.NotEmpty(model.AllGradeLevels);
    }
}
