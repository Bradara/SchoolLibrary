using SchoolLibrary.Data;
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
        var result = await _service.CreateAsync("  Биология  ");

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
        var result = await _service.CreateAsync(name!);

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.Categories);
    }

    [Fact]
    public async Task CreateAsync_NameLongerThanLimit_ReturnsInvalid()
    {
        var name = new string('а', CategoryService.MaxNameLength + 1);

        var result = await _service.CreateAsync(name);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.Categories);
    }

    [Fact]
    public async Task CreateAsync_NameOfMaxLength_Succeeds()
    {
        var result = await _service.CreateAsync(new string('а', CategoryService.MaxNameLength));

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

        var result = await _service.CreateAsync(name);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Contains("вече съществува", result.Message);
        Assert.Single(_context.Categories);
    }

    // ---- UpdateAsync ----

    [Fact]
    public async Task UpdateAsync_ValidName_RenamesCategory()
    {
        var category = await AddCategoryAsync("Стар");

        var result = await _service.UpdateAsync(category.Id, "Нов");

        Assert.True(result.Succeeded);
        Assert.Equal("Нов", _context.Categories.Single().Name);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNotFound()
    {
        var result = await _service.UpdateAsync(999, "Нов");

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_NameUsedByAnotherCategory_ReturnsConflict()
    {
        await AddCategoryAsync("Музика");
        var other = await AddCategoryAsync("Танци");

        var result = await _service.UpdateAsync(other.Id, "музика");

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal("Танци", _context.Categories.Single(c => c.Id == other.Id).Name);
    }

    [Fact]
    public async Task UpdateAsync_OnlyChangingCaseOfOwnName_Succeeds()
    {
        var category = await AddCategoryAsync("музика");

        var result = await _service.UpdateAsync(category.Id, "Музика");

        Assert.True(result.Succeeded);
        Assert.Equal("Музика", _context.Categories.Single().Name);
    }

    [Fact]
    public async Task UpdateAsync_EmptyName_ReturnsInvalid()
    {
        var category = await AddCategoryAsync("Музика");

        var result = await _service.UpdateAsync(category.Id, " ");

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Музика", _context.Categories.Single().Name);
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
