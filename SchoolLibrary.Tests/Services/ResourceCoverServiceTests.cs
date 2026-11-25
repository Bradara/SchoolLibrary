using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

public class ResourceCoverServiceTests
{
    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly InMemoryFileStorage _storage = new();
    private readonly ResourceCoverService _service;

    public ResourceCoverServiceTests()
    {
        _service = new ResourceCoverService(_context, _storage);
    }

    // An uploaded file; with bytes it is also put into the storage
    private ResourceFile AddFile(string contentType, byte[]? bytes, string? coverKey = null)
    {
        var key = $"resources/{Guid.NewGuid():N}";
        var resource = new Resource
        {
            Title = "Книга",
            Author = "Автор",
            Kind = ResourceKind.Upload,
            File = new ResourceFile
            {
                StorageKey = key,
                FileName = "книга",
                ContentType = contentType,
                Sha256 = Guid.NewGuid().ToString("N"),
                CoverKey = coverKey,
                CoverContentType = coverKey == null ? null : "image/jpeg"
            }
        };
        _context.Resources.Add(resource);
        _context.SaveChanges();
        if (bytes != null)
        {
            _storage.Files[key] = bytes;
        }

        return resource.File;
    }

    private const string Epub = "application/epub+zip";

    [Fact]
    public async Task CountBooksWithoutCover_CountsOnlyEpubsWithoutACover()
    {
        AddFile(Epub, null);
        AddFile(Epub, null, coverKey: "covers/a.jpg");
        AddFile("application/pdf", null);

        Assert.Equal(1, await _service.CountBooksWithoutCoverAsync());
    }

    [Fact]
    public async Task ExtractMissing_StoresTheCoverOfABookWithOne()
    {
        var book = AddFile(Epub, TestEpubs.Book(TestEpubs.JpegCover));

        var result = await _service.ExtractMissingAsync();

        Assert.Equal(new CoverExtractionResult(1, 1, 0, 0), result);
        var saved = _context.ResourceFiles.Single(f => f.Id == book.Id);
        Assert.StartsWith(ResourceCovers.KeyPrefix, saved.CoverKey);
        Assert.Equal("image/jpeg", saved.CoverContentType);
        Assert.Equal(TestEpubs.JpegCover, _storage.Files[saved.CoverKey!]);
        Assert.Equal(0, await _service.CountBooksWithoutCoverAsync());
    }

    [Fact]
    public async Task ExtractMissing_CountsBooksWithoutACoverAndMissingFiles()
    {
        AddFile(Epub, TestEpubs.Book(cover: null));
        AddFile(Epub, bytes: null);

        var result = await _service.ExtractMissingAsync();

        Assert.Equal(new CoverExtractionResult(2, 0, 1, 1), result);
        Assert.All(_context.ResourceFiles, f => Assert.Null(f.CoverKey));
        Assert.DoesNotContain(_storage.Files.Keys, k => k.StartsWith(ResourceCovers.KeyPrefix));
    }

    [Fact]
    public async Task ExtractMissing_LeavesBooksWithACoverAndOtherFilesAlone()
    {
        AddFile(Epub, TestEpubs.Book(TestEpubs.JpegCover), coverKey: "covers/old.jpg");
        AddFile("application/pdf", new byte[] { 1, 2, 3 });

        var result = await _service.ExtractMissingAsync();

        Assert.Equal(new CoverExtractionResult(0, 0, 0, 0), result);
        Assert.Equal("covers/old.jpg", _context.ResourceFiles.Single(f => f.ContentType == Epub).CoverKey);
    }

    [Fact]
    public async Task ExtractMissing_DoesTheSameBookOnlyOnce()
    {
        AddFile(Epub, TestEpubs.Book(TestEpubs.JpegCover));

        await _service.ExtractMissingAsync();
        var second = await _service.ExtractMissingAsync();

        Assert.Equal(new CoverExtractionResult(0, 0, 0, 0), second);
        Assert.Single(_storage.Files.Keys, k => k.StartsWith(ResourceCovers.KeyPrefix));
    }
}
