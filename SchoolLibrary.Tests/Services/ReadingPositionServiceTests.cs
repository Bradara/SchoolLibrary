using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Reading;

namespace SchoolLibrary.Tests.Services;

public class ReadingPositionServiceTests
{
    private const string Cfi = "epubcfi(/6/4!/4/2/1:0)";

    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly ReadingPositionService _service;

    public ReadingPositionServiceTests()
    {
        _service = new ReadingPositionService(_context);
        _context.Users.AddRange(
            new ApplicationUser { Id = "ivan", UserName = "ivan", FirstName = "Иван", LastName = "Петров" },
            new ApplicationUser { Id = "maria", UserName = "maria", FirstName = "Мария", LastName = "Георгиева" });
        _context.SaveChanges();
    }

    private async Task<Resource> AddUploadAsync(string title, string contentType = ReadingPositionService.EpubContentType)
    {
        var resource = new Resource
        {
            Title = title,
            Author = "Автор",
            Url = "https://example.com/" + title,
            Kind = ResourceKind.Upload,
            CreatedOn = DateTime.UtcNow,
            File = new ResourceFile
            {
                StorageKey = "resources/" + title,
                FileName = title + ".epub",
                ContentType = contentType,
                SizeBytes = 10,
                UploadedOn = DateTime.UtcNow
            }
        };
        _context.Resources.Add(resource);
        await _context.SaveChangesAsync();
        return resource;
    }

    // ---------- SaveAsync ----------

    [Fact]
    public async Task SaveAsync_StoresNewPosition()
    {
        var book = await AddUploadAsync("A");
        var before = DateTime.UtcNow;

        var result = await _service.SaveAsync("ivan", book.Id, Cfi, 42);

        Assert.True(result.Succeeded);
        var saved = Assert.Single(_context.ReadingPositions);
        Assert.Equal("ivan", saved.UserId);
        Assert.Equal(book.Id, saved.ResourceId);
        Assert.Equal(Cfi, saved.Cfi);
        Assert.Equal(42, saved.Percent);
        Assert.True(saved.UpdatedOn >= before);
    }

    [Fact]
    public async Task SaveAsync_UpdatesExistingPositionInPlace()
    {
        var book = await AddUploadAsync("A");
        await _service.SaveAsync("ivan", book.Id, Cfi, 10);

        var result = await _service.SaveAsync("ivan", book.Id, "epubcfi(/6/8!/4/2/1:5)", 55);

        Assert.True(result.Succeeded);
        var saved = Assert.Single(_context.ReadingPositions);
        Assert.Equal("epubcfi(/6/8!/4/2/1:5)", saved.Cfi);
        Assert.Equal(55, saved.Percent);
    }

    [Fact]
    public async Task SaveAsync_KeepsPositionsOfUsersApart()
    {
        var book = await AddUploadAsync("A");

        await _service.SaveAsync("ivan", book.Id, Cfi, 10);
        await _service.SaveAsync("maria", book.Id, "epubcfi(/6/10!/4/2/1:0)", 80);

        Assert.Equal(2, _context.ReadingPositions.Count());
        Assert.Equal(10, (await _service.GetAsync("ivan", book.Id))!.Percent);
        Assert.Equal(80, (await _service.GetAsync("maria", book.Id))!.Percent);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(150, 100)]
    public async Task SaveAsync_ClampsPercent(int sent, int expected)
    {
        var book = await AddUploadAsync("A");

        await _service.SaveAsync("ivan", book.Id, Cfi, sent);

        Assert.Equal(expected, _context.ReadingPositions.Single().Percent);
    }

    [Fact]
    public async Task SaveAsync_TrimsCfi()
    {
        var book = await AddUploadAsync("A");

        await _service.SaveAsync("ivan", book.Id, "  " + Cfi + " ", 1);

        Assert.Equal(Cfi, _context.ReadingPositions.Single().Cfi);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/6/4!/4/2/1:0")]
    [InlineData("epubcfi(/6/4!/4/2/1:0")]
    [InlineData("javascript:alert(1)")]
    [InlineData("epubcfi(/6/4\n)")]
    public async Task SaveAsync_RefusesInvalidCfi(string? cfi)
    {
        var book = await AddUploadAsync("A");

        var result = await _service.SaveAsync("ivan", book.Id, cfi, 10);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.ReadingPositions);
    }

    [Fact]
    public async Task SaveAsync_RefusesTooLongCfi()
    {
        var book = await AddUploadAsync("A");
        var cfi = "epubcfi(" + new string('4', ReadingPosition.CfiMaxLength) + ")";

        var result = await _service.SaveAsync("ivan", book.Id, cfi, 10);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.ReadingPositions);
    }

    [Fact]
    public async Task SaveAsync_AcceptsCfiOfMaxLength()
    {
        var book = await AddUploadAsync("A");
        var cfi = "epubcfi(" + new string('4', ReadingPosition.CfiMaxLength - 9) + ")";

        var result = await _service.SaveAsync("ivan", book.Id, cfi, 10);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task SaveAsync_RefusesMissingResource()
    {
        var result = await _service.SaveAsync("ivan", 999, Cfi, 10);

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Empty(_context.ReadingPositions);
    }

    [Fact]
    public async Task SaveAsync_RefusesUploadThatIsNotEpub()
    {
        var pdf = await AddUploadAsync("A", "application/pdf");

        var result = await _service.SaveAsync("ivan", pdf.Id, Cfi, 10);

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Empty(_context.ReadingPositions);
    }

    [Fact]
    public async Task SaveAsync_RefusesResourceWithoutFile()
    {
        var link = new Resource { Title = "Връзка", Author = "Автор", Url = "https://example.com/x", CreatedOn = DateTime.UtcNow };
        _context.Resources.Add(link);
        await _context.SaveChangesAsync();

        var result = await _service.SaveAsync("ivan", link.Id, Cfi, 10);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task SaveAsync_KeepsOnlyMostRecentBooksOfUser()
    {
        var books = new List<Resource>();
        for (var i = 0; i < ReadingPositionService.MaxBooksPerUser + 2; i++)
        {
            books.Add(await AddUploadAsync("B" + i));
        }

        // Older positions first: B0 and B1 are read longest ago
        var start = DateTime.UtcNow.AddDays(-10);
        for (var i = 0; i < books.Count - 1; i++)
        {
            _context.ReadingPositions.Add(new ReadingPosition
            {
                UserId = "ivan", ResourceId = books[i].Id, Cfi = Cfi, Percent = i, UpdatedOn = start.AddHours(i)
            });
        }
        await _context.SaveChangesAsync();

        await _service.SaveAsync("ivan", books[^1].Id, Cfi, 99);

        var kept = _context.ReadingPositions.Where(p => p.UserId == "ivan").Select(p => p.ResourceId).ToHashSet();
        Assert.Equal(ReadingPositionService.MaxBooksPerUser, kept.Count);
        Assert.DoesNotContain(books[0].Id, kept);
        Assert.DoesNotContain(books[1].Id, kept);
        Assert.Contains(books[^1].Id, kept);
    }

    [Fact]
    public async Task SaveAsync_RereadingOldBookKeepsItAndForgetsNextOldest()
    {
        var books = new List<Resource>();
        for (var i = 0; i < ReadingPositionService.MaxBooksPerUser; i++)
        {
            books.Add(await AddUploadAsync("B" + i));
            _context.ReadingPositions.Add(new ReadingPosition
            {
                UserId = "ivan", ResourceId = books[i].Id, Cfi = Cfi, UpdatedOn = DateTime.UtcNow.AddHours(i - 10)
            });
        }
        await _context.SaveChangesAsync();

        // The oldest book is opened again: nothing is forgotten, it just becomes the newest
        await _service.SaveAsync("ivan", books[0].Id, Cfi, 20);
        Assert.Equal(ReadingPositionService.MaxBooksPerUser, _context.ReadingPositions.Count());

        // A sixth book pushes out the oldest one, which is now B1
        var extra = await AddUploadAsync("Extra");
        await _service.SaveAsync("ivan", extra.Id, Cfi, 1);

        var kept = _context.ReadingPositions.Select(p => p.ResourceId).ToHashSet();
        Assert.Contains(books[0].Id, kept);
        Assert.DoesNotContain(books[1].Id, kept);
        Assert.Contains(extra.Id, kept);
    }

    [Fact]
    public async Task SaveAsync_DoesNotForgetBooksOfOtherUsers()
    {
        var mariaBook = await AddUploadAsync("M");
        _context.ReadingPositions.Add(new ReadingPosition
        {
            UserId = "maria", ResourceId = mariaBook.Id, Cfi = Cfi, UpdatedOn = DateTime.UtcNow.AddYears(-1)
        });
        await _context.SaveChangesAsync();

        for (var i = 0; i < ReadingPositionService.MaxBooksPerUser + 1; i++)
        {
            var book = await AddUploadAsync("I" + i);
            await _service.SaveAsync("ivan", book.Id, Cfi, i);
        }

        Assert.Single(_context.ReadingPositions, p => p.UserId == "maria");
        Assert.Equal(ReadingPositionService.MaxBooksPerUser, _context.ReadingPositions.Count(p => p.UserId == "ivan"));
    }

    // ---------- GetAsync ----------

    [Fact]
    public async Task GetAsync_ReturnsNullWithoutSavedPosition()
    {
        var book = await AddUploadAsync("A");

        Assert.Null(await _service.GetAsync("ivan", book.Id));
    }

    [Fact]
    public async Task GetAsync_ReturnsSavedPosition()
    {
        var book = await AddUploadAsync("A");
        await _service.SaveAsync("ivan", book.Id, Cfi, 37);

        var position = await _service.GetAsync("ivan", book.Id);

        Assert.NotNull(position);
        Assert.Equal(Cfi, position.Cfi);
        Assert.Equal(37, position.Percent);
    }

    // ---------- Cascades ----------

    [Fact]
    public async Task DeletingBook_RemovesItsPositions()
    {
        var book = await AddUploadAsync("A");
        await _service.SaveAsync("ivan", book.Id, Cfi, 37);

        _context.Resources.Remove(book);
        await _context.SaveChangesAsync();

        Assert.Empty(_context.ReadingPositions);
    }
}
