using SchoolLibrary.Data;
using SchoolLibrary.Data.Common;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Tests.Services;

public class CategoryServiceTests
{
    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly CategoryService _service;

    public CategoryServiceTests()
    {
        _service = new CategoryService(_context);
    }

    private async Task<Category> AddCategoryAsync(string name)
    {
        var category = new Category { Name = name };
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();
        return category;
    }

    private static Resource NewResource(string title) => new()
    {
        Title = title,
        Url = "https://example.com",
        Author = "Author",
        OwnerId = "owner-1",
        CreatedOn = DateTime.UtcNow
    };

    // ---- GetAllAsync ----

    [Fact]
    public async Task GetAllAsync_ReturnsCategoriesOrderedByName()
    {
        await AddCategoryAsync("Физика");
        await AddCategoryAsync("Анатомия");
        await AddCategoryAsync("Музика");

        var result = await _service.GetAllAsync();

        Assert.Equal(new[] { "Анатомия", "Музика", "Физика" }, result.Select(c => c.Name));
    }

    [Fact]
    public async Task GetAllAsync_WithNoCategories_ReturnsEmptyList()
    {
        var result = await _service.GetAllAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOptionsAsync_ReturnsCategoriesOrderedByName()
    {
        await AddCategoryAsync("Физика");
        await AddCategoryAsync("Анатомия");

        var result = await _service.GetOptionsAsync();

        Assert.Equal(new[] { "Анатомия", "Физика" }, result.Select(c => c.Name));
    }

    // ---- GetByIdAsync / GetWithResourcesAsync ----

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsCategory()
    {
        var category = await AddCategoryAsync("Химия");

        var result = await _service.GetByIdAsync(category.Id);

        Assert.NotNull(result);
        Assert.Equal("Химия", result!.Name);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _service.GetByIdAsync(999));
    }

    [Fact]
    public async Task GetWithResourcesAsync_ReturnsCategoryWithItsResources()
    {
        var category = await AddCategoryAsync("История");
        var resource = NewResource("Рим");
        resource.Categories.Add(category);
        _context.Resources.Add(resource);
        await _context.SaveChangesAsync();

        var result = await _service.GetWithResourcesAsync(category.Id);

        Assert.NotNull(result);
        Assert.Single(result!.Resources);
        Assert.Equal("Рим", result.Resources.First().Title);
    }

    [Fact]
    public async Task GetWithResourcesAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _service.GetWithResourcesAsync(999));
    }

    // ---- CreateAsync ----

    [Fact]
    public async Task CreateAsync_ValidName_SavesTrimmedCategory()
    {
        var result = await _service.CreateAsync(new CategoryInput("  Биология  ", null, null));

        Assert.True(result.Succeeded);
        Assert.Equal("Биология", result.Value!.Name);
        Assert.Single(_context.Categories);
        Assert.Equal("Биология", _context.Categories.Single().Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateAsync_EmptyName_ReturnsInvalid(string? name)
    {
        var result = await _service.CreateAsync(new CategoryInput(name!, null, null));

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.Categories);
    }

    [Fact]
    public async Task CreateAsync_NameLongerThanLimit_ReturnsInvalid()
    {
        var name = new string('а', CategoryService.MaxNameLength + 1);

        var result = await _service.CreateAsync(new CategoryInput(name, null, null));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.Categories);
    }

    [Fact]
    public async Task CreateAsync_NameOfMaxLength_Succeeds()
    {
        var result = await _service.CreateAsync(new CategoryInput(new string('а', CategoryService.MaxNameLength), null, null));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Музика")]
    [InlineData("музика")]
    [InlineData("МУЗИКА")]
    [InlineData("  Музика ")]
    public async Task CreateAsync_DuplicateName_IgnoringCaseAndSpaces_ReturnsConflict(string name)
    {
        await AddCategoryAsync("Музика");

        var result = await _service.CreateAsync(new CategoryInput(name, null, null));

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Contains("вече съществува", result.Message);
        Assert.Single(_context.Categories);
    }

    // ---- UpdateAsync ----

    [Fact]
    public async Task UpdateAsync_ValidName_RenamesCategory()
    {
        var category = await AddCategoryAsync("Стар");

        var result = await _service.UpdateAsync(category.Id, new CategoryInput("Нов", null, null));

        Assert.True(result.Succeeded);
        Assert.Equal("Нов", _context.Categories.Single().Name);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNotFound()
    {
        var result = await _service.UpdateAsync(999, new CategoryInput("Нов", null, null));

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_NameUsedByAnotherCategory_ReturnsConflict()
    {
        await AddCategoryAsync("Музика");
        var other = await AddCategoryAsync("Танци");

        var result = await _service.UpdateAsync(other.Id, new CategoryInput("музика", null, null));

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal("Танци", _context.Categories.Single(c => c.Id == other.Id).Name);
    }

    [Fact]
    public async Task UpdateAsync_OnlyChangingCaseOfOwnName_Succeeds()
    {
        var category = await AddCategoryAsync("музика");

        var result = await _service.UpdateAsync(category.Id, new CategoryInput("Музика", null, null));

        Assert.True(result.Succeeded);
        Assert.Equal("Музика", _context.Categories.Single().Name);
    }

    [Fact]
    public async Task UpdateAsync_EmptyName_ReturnsInvalid()
    {
        var category = await AddCategoryAsync("Музика");

        var result = await _service.UpdateAsync(category.Id, new CategoryInput(" ", null, null));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Музика", _context.Categories.Single().Name);
    }

    // ---- Icon and color ----

    [Fact]
    public async Task CreateAsync_WithoutALook_StandardSubjectGetsItsOwn()
    {
        var result = await _service.CreateAsync(new CategoryInput("Математика", null, " "));

        Assert.True(result.Succeeded);
        var saved = _context.Categories.Single();
        Assert.Equal(("bi-plus-slash-minus", "#2f6fed"), (saved.Icon, saved.Color));
    }

    [Fact]
    public async Task CreateAsync_WithoutALook_OtherSubjectGetsTheDefault()
    {
        await _service.CreateAsync(new CategoryInput("Астрономия", "", null));

        var saved = _context.Categories.Single();
        Assert.Equal((SubjectAppearance.DefaultIcon, SubjectAppearance.DefaultColor), (saved.Icon, saved.Color));
    }

    [Fact]
    public async Task CreateAsync_ChosenLook_IsSaved_AndWinsOverTheStandardOne()
    {
        var result = await _service.CreateAsync(new CategoryInput("Математика", " bi-calculator ", "#e03131"));

        Assert.True(result.Succeeded);
        var saved = _context.Categories.Single();
        Assert.Equal(("bi-calculator", "#e03131"), (saved.Icon, saved.Color));
    }

    [Fact]
    public async Task CreateAsync_OnlyTheColorChosen_TakesTheStandardIcon()
    {
        await _service.CreateAsync(new CategoryInput("Музика", null, "#2f6fed"));

        var saved = _context.Categories.Single();
        Assert.Equal(("bi-music-note-beamed", "#2f6fed"), (saved.Icon, saved.Color));
    }

    [Theory]
    [InlineData("bi-does-not-exist", null, "иконка")]
    [InlineData("bi-book\" onmouseover=\"alert(1)", null, "иконка")]
    [InlineData(null, "red", "цвят")]
    [InlineData(null, "#2f6fed;background:url(x)", "цвят")]
    [InlineData(null, "#123456", "цвят")]
    public async Task CreateAsync_LookOutsideTheLists_ReturnsInvalid(string? icon, string? color, string message)
    {
        var result = await _service.CreateAsync(new CategoryInput("Музика", icon, color));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains(message, result.Message);
        Assert.Empty(_context.Categories);
    }

    [Fact]
    public async Task UpdateAsync_WithoutALook_KeepsTheCurrentOne()
    {
        var category = await AddCategoryAsync("Стар");
        category.Icon = "bi-palette";
        category.Color = "#d6336c";
        await _context.SaveChangesAsync();

        // Even a standard name does not bring the standard look back: the administrator chose this one
        var result = await _service.UpdateAsync(category.Id, new CategoryInput("Математика", null, ""));

        Assert.True(result.Succeeded);
        var saved = _context.Categories.Single();
        Assert.Equal(("Математика", "bi-palette", "#d6336c"), (saved.Name, saved.Icon, saved.Color));
    }

    [Fact]
    public async Task UpdateAsync_ChosenLook_ChangesIt()
    {
        var category = await AddCategoryAsync("Музика");

        var result = await _service.UpdateAsync(category.Id, new CategoryInput("Музика", "bi-music-note-beamed", "#9c36b5"));

        Assert.True(result.Succeeded);
        var saved = _context.Categories.Single();
        Assert.Equal(("bi-music-note-beamed", "#9c36b5"), (saved.Icon, saved.Color));
    }

    [Theory]
    [InlineData("bi-x", "#9c36b5")]
    [InlineData("bi-book", "#FFFFFF")]
    public async Task UpdateAsync_LookOutsideTheLists_ReturnsInvalid_AndChangesNothing(string icon, string color)
    {
        var category = await AddCategoryAsync("Музика");

        var result = await _service.UpdateAsync(category.Id, new CategoryInput("Нова музика", icon, color));

        Assert.Equal(ServiceError.Invalid, result.Error);
        var saved = _context.Categories.Single();
        Assert.Equal(("Музика", SubjectAppearance.DefaultIcon, SubjectAppearance.DefaultColor), (saved.Name, saved.Icon, saved.Color));
    }

    // ---- DeleteAsync ----

    [Fact]
    public async Task DeleteAsync_CategoryWithoutResources_RemovesIt()
    {
        var category = await AddCategoryAsync("Музика");

        var result = await _service.DeleteAsync(category.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(_context.Categories);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsNotFound()
    {
        var result = await _service.DeleteAsync(999);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task DeleteAsync_CategoryWithResources_ReturnsConflictAndKeepsIt()
    {
        var category = await AddCategoryAsync("Музика");
        var resource = NewResource("Нотация");
        resource.Categories.Add(category);
        _context.Resources.Add(resource);
        await _context.SaveChangesAsync();

        var result = await _service.DeleteAsync(category.Id);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Contains("1 свързани ресурса", result.Message);
        Assert.Single(_context.Categories);
    }
}
