using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
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

    // ---------- PDF: the first page drawn by the administrator's browser ----------

    private const string Pdf = "application/pdf";
    private static readonly byte[] JpegPicture = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };

    [Fact]
    public async Task PdfsWithoutCover_AreCountedAndListedOldestFirst()
    {
        var first = AddFile(Pdf, null);
        AddFile(Pdf, null, coverKey: "covers/a.jpg");
        AddFile(Epub, null);
        var second = AddFile(Pdf, null);

        Assert.Equal(2, await _service.CountPdfsWithoutCoverAsync());
        var list = await _service.GetPdfsWithoutCoverAsync();
        Assert.Equal(new[] { first.ResourceId, second.ResourceId }, list.Select(p => p.ResourceId));
        Assert.Equal("Книга", list[0].Title);
    }

    [Fact]
    public async Task SetPdfCover_StoresThePicture()
    {
        var pdf = AddFile(Pdf, null);

        var result = await _service.SetPdfCoverAsync(pdf.ResourceId, JpegPicture);

        Assert.True(result.Succeeded);
        var saved = _context.ResourceFiles.Single(f => f.Id == pdf.Id);
        Assert.StartsWith(ResourceCovers.KeyPrefix, saved.CoverKey);
        Assert.Equal("image/jpeg", saved.CoverContentType);
        Assert.Equal(JpegPicture, _storage.Files[saved.CoverKey!]);
        Assert.Equal(0, await _service.CountPdfsWithoutCoverAsync());
    }

    [Fact]
    public async Task SetPdfCover_RefusesAFileThatIsNotAPdf()
    {
        var book = AddFile(Epub, null);

        var result = await _service.SetPdfCoverAsync(book.ResourceId, JpegPicture);

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task SetPdfCover_RefusesAMissingResource()
    {
        var result = await _service.SetPdfCoverAsync(999, JpegPicture);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task SetPdfCover_DoesNotReplaceACover()
    {
        var pdf = AddFile(Pdf, null, coverKey: "covers/old.jpg");

        var result = await _service.SetPdfCoverAsync(pdf.ResourceId, JpegPicture);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal("covers/old.jpg", _context.ResourceFiles.Single().CoverKey);
        Assert.Empty(_storage.Files);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 0 })]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x3E })]
    public async Task SetPdfCover_RefusesAPictureThatIsNotAccepted(byte[]? picture)
    {
        var pdf = AddFile(Pdf, null);

        var result = await _service.SetPdfCoverAsync(pdf.ResourceId, picture);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Null(_context.ResourceFiles.Single().CoverKey);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task SetPdfCover_RefusesATooLargePicture()
    {
        var pdf = AddFile(Pdf, null);
        var large = JpegPicture.Concat(new byte[ResourceCovers.MaxPdfCoverBytes]).ToArray();

        var result = await _service.SetPdfCoverAsync(pdf.ResourceId, large);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }
}
