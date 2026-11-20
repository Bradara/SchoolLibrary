using Moq;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.OnlineLibraries;
using SchoolLibrary.Services.SiteIcons;
using SchoolLibrary.Services.Storage;

namespace SchoolLibrary.Tests.Services;

public class LibraryIconServiceTests
{
    private static readonly SiteIcon PngIcon = new(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1 }, "image/png");

    private sealed class MemoryStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();

        public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            Files[key] = copy.ToArray();
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Files.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Files.ContainsKey(key));
    }

    private readonly Mock<ISiteIconFetcher> _fetcher = new();
    private readonly MemoryStorage _storage = new();

    private LibraryIconService Service(ApplicationDbContext context) => new(context, _fetcher.Object, _storage);

    private static async Task<OnlineLibrary> AddLibraryAsync(ApplicationDbContext context, string url = "https://chitanka.info")
    {
        var library = new OnlineLibrary { Name = "Читанка", Url = url };
        context.OnlineLibraries.Add(library);
        await context.SaveChangesAsync();
        return library;
    }

    private void SiteReturns(SiteIcon? icon) =>
        _fetcher.Setup(f => f.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(icon);

    [Fact]
    public async Task RefreshAsync_StoresTheIcon_AndRemembersTheAddress()
    {
        using var context = TestDbContextFactory.Create();
        var library = await AddLibraryAsync(context);
        SiteReturns(PngIcon);

        var result = await Service(context).RefreshAsync(library.Id);

        Assert.True(result.Succeeded);
        var saved = context.OnlineLibraries.Single();
        Assert.True(saved.HasSiteIcon);
        Assert.StartsWith(LibraryIconService.KeyPrefix, saved.SiteIconKey);
        Assert.EndsWith(".png", saved.SiteIconKey);
        Assert.Equal("image/png", saved.SiteIconContentType);
        Assert.Equal("https://chitanka.info", saved.SiteIconSourceUrl);
        Assert.Equal(PngIcon.Content, _storage.Files[saved.SiteIconKey!]);
        _fetcher.Verify(f => f.FetchAsync("https://chitanka.info", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_TheIconAlreadyBelongsToTheAddress_DownloadsNothing()
    {
        using var context = TestDbContextFactory.Create();
        var library = await AddLibraryAsync(context);
        SiteReturns(PngIcon);
        var service = Service(context);
        await service.RefreshAsync(library.Id);

        var result = await service.RefreshAsync(library.Id);

        Assert.True(result.Succeeded);
        _fetcher.Verify(f => f.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_Forced_ReplacesTheIcon_AndRemovesTheOldFile()
    {
        using var context = TestDbContextFactory.Create();
        var library = await AddLibraryAsync(context);
        SiteReturns(PngIcon);
        var service = Service(context);
        await service.RefreshAsync(library.Id);
        var oldKey = library.SiteIconKey!;

        SiteReturns(new SiteIcon(new byte[] { 0, 0, 1, 0, 9 }, "image/x-icon"));
        var result = await service.RefreshAsync(library.Id, force: true);

        Assert.True(result.Succeeded);
        Assert.NotEqual(oldKey, library.SiteIconKey);
        Assert.EndsWith(".ico", library.SiteIconKey);
        Assert.Equal(new[] { library.SiteIconKey }, _storage.Files.Keys);
    }

    [Fact]
    public async Task RefreshAsync_TheAddressChanged_DownloadsAgain()
    {
        using var context = TestDbContextFactory.Create();
        var library = await AddLibraryAsync(context);
        SiteReturns(PngIcon);
        var service = Service(context);
        await service.RefreshAsync(library.Id);

        library.Url = "https://4eti.me";
        await context.SaveChangesAsync();
        await service.RefreshAsync(library.Id);

        Assert.Equal("https://4eti.me", library.SiteIconSourceUrl);
        Assert.Single(_storage.Files);
        _fetcher.Verify(f => f.FetchAsync("https://4eti.me", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_NoIconAfterTheAddressChanged_DropsTheIconOfTheOldSite()
    {
        using var context = TestDbContextFactory.Create();
        var library = await AddLibraryAsync(context);
        SiteReturns(PngIcon);
        var service = Service(context);
        await service.RefreshAsync(library.Id);

        library.Url = "https://4eti.me";
        await context.SaveChangesAsync();
        SiteReturns(null);
        var result = await service.RefreshAsync(library.Id);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("не можа", result.Message);
        Assert.False(library.HasSiteIcon);
        Assert.Null(library.SiteIconSourceUrl);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task RefreshAsync_ForcedWithoutIcon_KeepsTheWorkingIconOfTheSameAddress()
    {
        using var context = TestDbContextFactory.Create();
        var library = await AddLibraryAsync(context);
        SiteReturns(PngIcon);
        var service = Service(context);
        await service.RefreshAsync(library.Id);
        var key = library.SiteIconKey;

        SiteReturns(null);
        var result = await service.RefreshAsync(library.Id, force: true);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal(key, library.SiteIconKey);
        Assert.True(_storage.Files.ContainsKey(key!));
    }

    [Fact]
    public async Task RefreshAsync_AnUnexpectedImageType_IsNotStored()
    {
        using var context = TestDbContextFactory.Create();
        var library = await AddLibraryAsync(context);
        SiteReturns(new SiteIcon(new byte[] { 1 }, "image/svg+xml"));

        var result = await Service(context).RefreshAsync(library.Id);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task RefreshAsync_UnknownLibrary_ReturnsNotFound()
    {
        using var context = TestDbContextFactory.Create();

        var result = await Service(context).RefreshAsync(99);

        Assert.Equal(ServiceError.NotFound, result.Error);
        _fetcher.Verify(f => f.FetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenAsync_ReturnsTheStoredBytesAndType()
    {
        using var context = TestDbContextFactory.Create();
        var library = await AddLibraryAsync(context);
        SiteReturns(PngIcon);
        var service = Service(context);
        await service.RefreshAsync(library.Id);

        var icon = await service.OpenAsync(library.Id);

        Assert.Equal("image/png", icon!.ContentType);
        using var copy = new MemoryStream();
        await icon.Content.CopyToAsync(copy);
        Assert.Equal(PngIcon.Content, copy.ToArray());
    }

    [Fact]
    public async Task OpenAsync_NoIconOrAMissingFile_ReturnsNull()
    {
        using var context = TestDbContextFactory.Create();
        var withoutIcon = await AddLibraryAsync(context);
        var lostFile = new OnlineLibrary
        {
            Name = "Чети", Url = "https://4eti.me",
            SiteIconKey = "libraries/gone.png", SiteIconContentType = "image/png", SiteIconSourceUrl = "https://4eti.me"
        };
        context.OnlineLibraries.Add(lostFile);
        await context.SaveChangesAsync();
        var service = Service(context);

        Assert.Null(await service.OpenAsync(withoutIcon.Id));
        Assert.Null(await service.OpenAsync(lostFile.Id));
        Assert.Null(await service.OpenAsync(999));
    }

    [Fact]
    public async Task DeleteStoredAsync_RemovesTheFile_AndIgnoresALibraryWithoutIcon()
    {
        _storage.Files["libraries/a.png"] = new byte[] { 1 };
        using var context = TestDbContextFactory.Create();
        var service = Service(context);

        await service.DeleteStoredAsync(new OnlineLibrary { SiteIconKey = "libraries/a.png" });
        await service.DeleteStoredAsync(new OnlineLibrary());

        Assert.Empty(_storage.Files);
    }

    [Fact]
    public void HasSiteIcon_NeedsBothTheKeyAndTheType()
    {
        Assert.False(new OnlineLibrary().HasSiteIcon);
        Assert.False(new OnlineLibrary { SiteIconKey = "k" }.HasSiteIcon);
        Assert.True(new OnlineLibrary { SiteIconKey = "k", SiteIconContentType = "image/png" }.HasSiteIcon);
    }
}
