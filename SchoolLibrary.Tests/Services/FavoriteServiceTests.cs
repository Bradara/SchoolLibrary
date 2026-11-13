using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Favorites;

namespace SchoolLibrary.Tests.Services;

public class FavoriteServiceTests
{
    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly FavoriteService _service;

    private readonly ApplicationUser _ivan = new() { Id = "ivan", UserName = "ivan", FirstName = "Иван", LastName = "Петров" };
    private readonly ApplicationUser _maria = new() { Id = "maria", UserName = "maria", FirstName = "Мария", LastName = "Георгиева" };

    public FavoriteServiceTests()
    {
        _service = new FavoriteService(_context);
        _context.Users.AddRange(_ivan, _maria);
        _context.SaveChanges();
    }

    private async Task<Resource> AddResourceAsync(string title)
    {
        var resource = new Resource { Title = title, Author = "Автор", Url = "https://example.com/" + title, CreatedOn = DateTime.UtcNow };
        _context.Resources.Add(resource);
        await _context.SaveChangesAsync();
        return resource;
    }

    private async Task SaveFavoriteAsync(string userId, int resourceId, DateTime savedOn)
    {
        _context.Favorites.Add(new Favorite { UserId = userId, ResourceId = resourceId, CreatedOn = savedOn });
        await _context.SaveChangesAsync();
    }

    // ---------- AddAsync ----------

    [Fact]
    public async Task AddAsync_SavesFavoriteWithCreationTime()
    {
        var resource = await AddResourceAsync("A");
        var before = DateTime.UtcNow;

        var result = await _service.AddAsync("ivan", resource.Id);

        Assert.True(result.Succeeded);
        var saved = Assert.Single(_context.Favorites);
        Assert.Equal("ivan", saved.UserId);
        Assert.Equal(resource.Id, saved.ResourceId);
        Assert.True(saved.CreatedOn >= before);
    }

    [Fact]
    public async Task AddAsync_SameResourceTwice_IsSavedOnce()
    {
        var resource = await AddResourceAsync("A");

        await _service.AddAsync("ivan", resource.Id);
        var second = await _service.AddAsync("ivan", resource.Id);

        Assert.True(second.Succeeded);
        Assert.Single(_context.Favorites);
    }

    [Fact]
    public async Task AddAsync_TwoUsersCanSaveTheSameResource()
    {
        var resource = await AddResourceAsync("A");

        await _service.AddAsync("ivan", resource.Id);
        await _service.AddAsync("maria", resource.Id);

        Assert.Equal(2, _context.Favorites.Count());
    }

    [Fact]
    public async Task AddAsync_UnknownResource_ReturnsNotFoundAndSavesNothing()
    {
        var result = await _service.AddAsync("ivan", 999);

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Empty(_context.Favorites);
    }

    // ---------- RemoveAsync ----------

    [Fact]
    public async Task RemoveAsync_RemovesOnlyThatUsersFavorite()
    {
        var resource = await AddResourceAsync("A");
        await SaveFavoriteAsync("ivan", resource.Id, DateTime.UtcNow);
        await SaveFavoriteAsync("maria", resource.Id, DateTime.UtcNow);

        var result = await _service.RemoveAsync("ivan", resource.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("maria", Assert.Single(_context.Favorites).UserId);
    }

    [Fact]
    public async Task RemoveAsync_NotSaved_IsNotAnError()
    {
        var resource = await AddResourceAsync("A");

        var result = await _service.RemoveAsync("ivan", resource.Id);

        Assert.True(result.Succeeded);
    }

    // ---------- reading ----------

    [Fact]
    public async Task GetFavoriteIdsAsync_ReturnsOnlyMySavedAmongTheGivenIds()
    {
        var a = await AddResourceAsync("A");
        var b = await AddResourceAsync("B");
        var c = await AddResourceAsync("C");
        await SaveFavoriteAsync("ivan", a.Id, DateTime.UtcNow);
        await SaveFavoriteAsync("ivan", c.Id, DateTime.UtcNow);
        await SaveFavoriteAsync("maria", b.Id, DateTime.UtcNow);

        var ids = await _service.GetFavoriteIdsAsync("ivan", new[] { a.Id, b.Id });

        Assert.Equal(new[] { a.Id }, ids);
    }

    [Fact]
    public async Task GetFavoriteIdsAsync_NoIds_ReturnsEmpty()
    {
        Assert.Empty(await _service.GetFavoriteIdsAsync("ivan", Array.Empty<int>()));
    }

    [Fact]
    public async Task IsFavoriteAsync_ReflectsOnlyTheGivenUser()
    {
        var resource = await AddResourceAsync("A");
        await SaveFavoriteAsync("ivan", resource.Id, DateTime.UtcNow);

        Assert.True(await _service.IsFavoriteAsync("ivan", resource.Id));
        Assert.False(await _service.IsFavoriteAsync("maria", resource.Id));
    }

    [Fact]
    public async Task CountAsync_CountsAllUsersWhoSavedTheResource()
    {
        var a = await AddResourceAsync("A");
        var b = await AddResourceAsync("B");
        await SaveFavoriteAsync("ivan", a.Id, DateTime.UtcNow);
        await SaveFavoriteAsync("maria", a.Id, DateTime.UtcNow);
        await SaveFavoriteAsync("maria", b.Id, DateTime.UtcNow);

        Assert.Equal(2, await _service.CountAsync(a.Id));
        Assert.Equal(0, await _service.CountAsync(999));
    }

    // ---------- GetFavoritesAsync ----------

    [Fact]
    public async Task GetFavoritesAsync_ReturnsOnlyMineMostRecentlySavedFirst()
    {
        var a = await AddResourceAsync("A");
        var b = await AddResourceAsync("B");
        var c = await AddResourceAsync("C");
        await SaveFavoriteAsync("ivan", a.Id, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SaveFavoriteAsync("ivan", b.Id, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        await SaveFavoriteAsync("maria", c.Id, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await _service.GetFavoritesAsync("ivan", 1);

        Assert.Equal(new[] { "B", "A" }, result.Items.Select(r => r.Title));
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task GetFavoritesAsync_LoadsSubjectsGradesAndLicense()
    {
        var resource = await AddResourceAsync("A");
        resource.Categories.Add(new Category { Name = "Математика" });
        resource.GradeLevels.Add(new GradeLevel { Number = 5 });
        resource.License = new License { Name = "CC BY 4.0" };
        await _context.SaveChangesAsync();
        await SaveFavoriteAsync("ivan", resource.Id, DateTime.UtcNow);

        var item = (await _service.GetFavoritesAsync("ivan", 1)).Items.Single();

        Assert.Equal("Математика", item.Categories.Single().Name);
        Assert.Equal(5, item.GradeLevels.Single().Number);
        Assert.Equal("CC BY 4.0", item.License!.Name);
    }

    [Fact]
    public async Task GetFavoritesAsync_PagesTheResults()
    {
        for (var i = 1; i <= 5; i++)
        {
            var resource = await AddResourceAsync("R" + i);
            await SaveFavoriteAsync("ivan", resource.Id, new DateTime(2026, 1, i, 0, 0, 0, DateTimeKind.Utc));
        }

        var second = await _service.GetFavoritesAsync("ivan", page: 2, pageSize: 2);

        Assert.Equal(new[] { "R3", "R2" }, second.Items.Select(r => r.Title));
        Assert.Equal(5, second.TotalCount);
        Assert.Equal(3, second.TotalPages);
        Assert.Equal(2, second.Page);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(99, 2)]
    public async Task GetFavoritesAsync_PageOutOfRange_IsClamped(int requested, int expected)
    {
        for (var i = 1; i <= 3; i++)
        {
            var resource = await AddResourceAsync("R" + i);
            await SaveFavoriteAsync("ivan", resource.Id, DateTime.UtcNow.AddMinutes(i));
        }

        var result = await _service.GetFavoritesAsync("ivan", requested, pageSize: 2);

        Assert.Equal(expected, result.Page);
    }

    [Fact]
    public async Task GetFavoritesAsync_NoFavorites_ReturnsEmptyFirstPage()
    {
        var result = await _service.GetFavoritesAsync("ivan", 1);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.Page);
    }
}
