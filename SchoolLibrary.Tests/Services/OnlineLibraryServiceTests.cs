using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.OnlineLibraries;

namespace SchoolLibrary.Tests.Services;

public class OnlineLibraryServiceTests
{
    private static OnlineLibraryInput Input(
        string name = "Читанка",
        string? description = "Описание",
        string url = "https://chitanka.info",
        string? icon = "bi-book",
        int order = 10) => new(name, description, url, icon, order);

    [Fact]
    public async Task GetAllAsync_OrdersBySortNumber_ThenByName()
    {
        using var context = TestDbContextFactory.Create();
        context.OnlineLibraries.AddRange(
            new OnlineLibrary { Name = "Б", Url = "https://b.bg", SortOrder = 1 },
            new OnlineLibrary { Name = "Я", Url = "https://y.bg", SortOrder = 0 },
            new OnlineLibrary { Name = "А", Url = "https://a.bg", SortOrder = 1 });
        await context.SaveChangesAsync();

        var result = await new OnlineLibraryService(context).GetAllAsync();

        Assert.Equal(new[] { "Я", "А", "Б" }, result.Select(l => l.Name));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTheLibrary_OrNullWhenMissing()
    {
        using var context = TestDbContextFactory.Create();
        var library = new OnlineLibrary { Name = "А", Url = "https://a.bg" };
        context.OnlineLibraries.Add(library);
        await context.SaveChangesAsync();
        var service = new OnlineLibraryService(context);

        Assert.Equal("А", (await service.GetByIdAsync(library.Id))!.Name);
        Assert.Null(await service.GetByIdAsync(999));
    }

    [Fact]
    public void GetIcons_ContainsTheDefaultIcon_AndNoDuplicates()
    {
        using var context = TestDbContextFactory.Create();

        var icons = new OnlineLibraryService(context).GetIcons();

        Assert.Contains(icons, i => i.Class == OnlineLibrary.DefaultIcon);
        Assert.Equal(icons.Count, icons.Select(i => i.Class).Distinct().Count());
    }

    [Fact]
    public async Task CreateAsync_SavesTheTrimmedValues()
    {
        using var context = TestDbContextFactory.Create();

        var result = await new OnlineLibraryService(context).CreateAsync(
            Input(name: "  Читанка ", description: " текст ", url: " https://chitanka.info ", icon: "bi-feather", order: 5));

        Assert.True(result.Succeeded);
        var saved = Assert.Single(context.OnlineLibraries);
        Assert.Equal("Читанка", saved.Name);
        Assert.Equal("текст", saved.Description);
        Assert.Equal("https://chitanka.info", saved.Url);
        Assert.Equal("bi-feather", saved.Icon);
        Assert.Equal(5, saved.SortOrder);
    }

    [Fact]
    public async Task CreateAsync_WithoutDescriptionOrIcon_StoresNullAndTheDefaultIcon()
    {
        using var context = TestDbContextFactory.Create();

        await new OnlineLibraryService(context).CreateAsync(Input(description: "  ", icon: null));

        var saved = Assert.Single(context.OnlineLibraries);
        Assert.Null(saved.Description);
        Assert.Equal(OnlineLibrary.DefaultIcon, saved.Icon);
    }

    [Theory]
    [InlineData("", "https://a.bg", "bi-book")]
    [InlineData("  ", "https://a.bg", "bi-book")]
    [InlineData("А", "", "bi-book")]
    [InlineData("А", "javascript:alert(1)", "bi-book")]
    [InlineData("А", "ftp://a.bg", "bi-book")]
    [InlineData("А", "не е адрес", "bi-book")]
    [InlineData("А", "https://a.bg", "bi-<script>")]
    public async Task CreateAsync_WithInvalidFields_IsRejectedAndNothingIsSaved(string name, string url, string icon)
    {
        using var context = TestDbContextFactory.Create();

        var result = await new OnlineLibraryService(context).CreateAsync(Input(name: name, url: url, icon: icon));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(context.OnlineLibraries);
    }

    [Fact]
    public async Task CreateAsync_WithTooLongFields_IsRejected()
    {
        using var context = TestDbContextFactory.Create();
        var service = new OnlineLibraryService(context);

        Assert.Equal(ServiceError.Invalid, (await service.CreateAsync(Input(name: new string('а', 101)))).Error);
        Assert.Equal(ServiceError.Invalid, (await service.CreateAsync(Input(description: new string('а', 301)))).Error);
        Assert.Equal(ServiceError.Invalid, (await service.CreateAsync(Input(url: "https://a.bg/" + new string('a', 500)))).Error);
    }

    [Fact]
    public async Task CreateAsync_WithAnExistingName_IgnoringCase_IsAConflict()
    {
        using var context = TestDbContextFactory.Create();
        var service = new OnlineLibraryService(context);
        await service.CreateAsync(Input(name: "Читанка", url: "https://chitanka.info"));

        var result = await service.CreateAsync(Input(name: "читанка", url: "https://other.bg"));

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Single(context.OnlineLibraries);
    }

    [Fact]
    public async Task CreateAsync_WithAnAddressAlreadyInTheCatalog_IsAConflictEvenWithWwwAndSlash()
    {
        using var context = TestDbContextFactory.Create();
        var service = new OnlineLibraryService(context);
        await service.CreateAsync(Input(name: "Читанка", url: "https://chitanka.info"));

        var result = await service.CreateAsync(Input(name: "Друго име", url: "https://www.chitanka.info/"));

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Contains("Читанка", result.Message);
    }

    [Fact]
    public async Task UpdateAsync_ChangesTheLibrary()
    {
        using var context = TestDbContextFactory.Create();
        var service = new OnlineLibraryService(context);
        var id = (await service.CreateAsync(Input())).Value!.Id;

        var result = await service.UpdateAsync(id, Input(name: "Нова", description: null, url: "https://new.bg", icon: "bi-globe2", order: 3));

        Assert.True(result.Succeeded);
        var saved = Assert.Single(context.OnlineLibraries);
        Assert.Equal("Нова", saved.Name);
        Assert.Null(saved.Description);
        Assert.Equal("https://new.bg", saved.Url);
        Assert.Equal("bi-globe2", saved.Icon);
        Assert.Equal(3, saved.SortOrder);
    }

    [Fact]
    public async Task UpdateAsync_KeepingItsOwnNameAndAddress_IsNotAConflict()
    {
        using var context = TestDbContextFactory.Create();
        var service = new OnlineLibraryService(context);
        var id = (await service.CreateAsync(Input())).Value!.Id;

        var result = await service.UpdateAsync(id, Input(description: "ново описание"));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task UpdateAsync_WithTheNameOrAddressOfAnotherLibrary_IsAConflict()
    {
        using var context = TestDbContextFactory.Create();
        var service = new OnlineLibraryService(context);
        await service.CreateAsync(Input(name: "А", url: "https://a.bg"));
        var id = (await service.CreateAsync(Input(name: "Б", url: "https://b.bg"))).Value!.Id;

        Assert.Equal(ServiceError.Conflict, (await service.UpdateAsync(id, Input(name: "а", url: "https://b.bg"))).Error);
        Assert.Equal(ServiceError.Conflict, (await service.UpdateAsync(id, Input(name: "Б", url: "https://a.bg"))).Error);
    }

    [Fact]
    public async Task UpdateAsync_WithInvalidFields_IsRejectedAndNothingChanges()
    {
        using var context = TestDbContextFactory.Create();
        var service = new OnlineLibraryService(context);
        var id = (await service.CreateAsync(Input())).Value!.Id;

        var result = await service.UpdateAsync(id, Input(url: "javascript:alert(1)"));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("https://chitanka.info", Assert.Single(context.OnlineLibraries).Url);
    }

    [Fact]
    public async Task UpdateAsync_OfAMissingLibrary_IsNotFound()
    {
        using var context = TestDbContextFactory.Create();

        var result = await new OnlineLibraryService(context).UpdateAsync(42, Input());

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheLibrary()
    {
        using var context = TestDbContextFactory.Create();
        var service = new OnlineLibraryService(context);
        var id = (await service.CreateAsync(Input())).Value!.Id;

        var result = await service.DeleteAsync(id);

        Assert.True(result.Succeeded);
        Assert.Empty(context.OnlineLibraries);
    }

    [Fact]
    public async Task DeleteAsync_OfAMissingLibrary_IsNotFound()
    {
        using var context = TestDbContextFactory.Create();

        var result = await new OnlineLibraryService(context).DeleteAsync(42);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }
}
