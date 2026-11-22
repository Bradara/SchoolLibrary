using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Reading;

namespace SchoolLibrary.Tests.Services;

public class BookmarkServiceTests
{
    private const string Cfi = "epubcfi(/6/4!/4/2/1:0)";

    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly BookmarkService _service;

    public BookmarkServiceTests()
    {
        _service = new BookmarkService(_context);
        _context.Users.AddRange(
            new ApplicationUser { Id = "ivan", UserName = "ivan", FirstName = "Иван", LastName = "Петров" },
            new ApplicationUser { Id = "maria", UserName = "maria", FirstName = "Мария", LastName = "Георгиева" });
        _context.SaveChanges();
    }

    private async Task<Resource> AddUploadAsync(string title, string contentType = EpubLocation.EpubContentType)
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

    // ---------- AddAsync ----------

    [Fact]
    public async Task AddAsync_SavesBookmark()
    {
        var book = await AddUploadAsync("A");

        var result = await _service.AddAsync("ivan", book.Id, Cfi, "Глава 1 — „Беше тъмна нощ…“", 12);

        Assert.True(result.Succeeded);
        var saved = Assert.Single(_context.Bookmarks);
        Assert.Equal(saved.Id, result.Value!.Id);
        Assert.Equal("ivan", saved.UserId);
        Assert.Equal(book.Id, saved.ResourceId);
        Assert.Equal(Cfi, saved.Cfi);
        Assert.Equal("Глава 1 — „Беше тъмна нощ…“", saved.Label);
        Assert.Equal(12, saved.Percent);
    }

    [Fact]
    public async Task AddAsync_SamePlaceAgain_ReturnsExistingBookmark()
    {
        var book = await AddUploadAsync("A");
        var first = await _service.AddAsync("ivan", book.Id, Cfi, "Първа", 12);

        var second = await _service.AddAsync("ivan", book.Id, " " + Cfi + " ", "Друго", 50);

        Assert.True(second.Succeeded);
        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Equal("Първа", second.Value.Label);
        Assert.Single(_context.Bookmarks);
    }

    [Fact]
    public async Task AddAsync_SamePlaceByAnotherUser_IsSeparate()
    {
        var book = await AddUploadAsync("A");

        await _service.AddAsync("ivan", book.Id, Cfi, "И", 1);
        await _service.AddAsync("maria", book.Id, Cfi, "М", 1);

        Assert.Equal(2, _context.Bookmarks.Count());
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(101, 100)]
    public async Task AddAsync_ClampsPercent(int sent, int expected)
    {
        var book = await AddUploadAsync("A");

        await _service.AddAsync("ivan", book.Id, Cfi, "x", sent);

        Assert.Equal(expected, _context.Bookmarks.Single().Percent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/6/4!/4")]
    [InlineData("javascript:alert(1)")]
    public async Task AddAsync_RefusesInvalidCfi(string? cfi)
    {
        var book = await AddUploadAsync("A");

        var result = await _service.AddAsync("ivan", book.Id, cfi, "x", 1);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.Bookmarks);
    }

    [Fact]
    public async Task AddAsync_RefusesResourceThatIsNotABook()
    {
        var pdf = await AddUploadAsync("A", "application/pdf");

        Assert.Equal(ServiceError.NotFound, (await _service.AddAsync("ivan", pdf.Id, Cfi, "x", 1)).Error);
        Assert.Equal(ServiceError.NotFound, (await _service.AddAsync("ivan", 999, Cfi, "x", 1)).Error);
        Assert.Empty(_context.Bookmarks);
    }

    [Fact]
    public async Task AddAsync_RefusesMoreThanTheLimitPerBook()
    {
        var book = await AddUploadAsync("A");
        for (var i = 0; i < BookmarkService.MaxPerBook; i++)
        {
            _context.Bookmarks.Add(new Bookmark
            {
                UserId = "ivan", ResourceId = book.Id, Cfi = $"epubcfi(/6/{i})", Label = "x", CreatedOn = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();

        var result = await _service.AddAsync("ivan", book.Id, Cfi, "x", 1);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal(BookmarkService.MaxPerBook, _context.Bookmarks.Count());

        // The limit is per user: another reader can still mark the book
        Assert.True((await _service.AddAsync("maria", book.Id, Cfi, "x", 1)).Succeeded);
    }

    // ---------- the label ----------

    [Theory]
    [InlineData(null, BookmarkService.DefaultLabel)]
    [InlineData("   ", BookmarkService.DefaultLabel)]
    [InlineData("\n\t", BookmarkService.DefaultLabel)]
    [InlineData("  Глава 1  ", "Глава 1")]
    [InlineData("Глава\n\n1 —\t„текст“", "Глава 1 — „текст“")]
    [InlineData("a\u0000b", "a b")]
    public void CleanLabel_MakesOneTidyLine(string? label, string expected)
    {
        Assert.Equal(expected, BookmarkService.CleanLabel(label));
    }

    [Fact]
    public void CleanLabel_CutsLongTextWithAnEllipsis()
    {
        var label = BookmarkService.CleanLabel(new string('а', Bookmark.LabelMaxLength + 50));

        Assert.Equal(Bookmark.LabelMaxLength, label.Length);
        Assert.EndsWith("…", label);
    }

    [Fact]
    public void CleanLabel_KeepsTextOfMaxLength()
    {
        var text = new string('а', Bookmark.LabelMaxLength);

        Assert.Equal(text, BookmarkService.CleanLabel(text));
    }

    // ---------- GetAsync ----------

    [Fact]
    public async Task GetAsync_ReturnsOnlyTheUsersBookmarksOfTheBookInReadingOrder()
    {
        var book = await AddUploadAsync("A");
        var other = await AddUploadAsync("B");
        await _service.AddAsync("ivan", book.Id, "epubcfi(/6/8)", "Края", 90);
        await _service.AddAsync("ivan", book.Id, "epubcfi(/6/2)", "Начало", 5);
        await _service.AddAsync("ivan", other.Id, "epubcfi(/6/4)", "Друга книга", 50);
        await _service.AddAsync("maria", book.Id, "epubcfi(/6/6)", "На Мария", 40);

        var list = await _service.GetAsync("ivan", book.Id);

        Assert.Equal(new[] { "Начало", "Края" }, list.Select(b => b.Label));
    }

    // ---------- RemoveAsync ----------

    [Fact]
    public async Task RemoveAsync_RemovesOwnBookmark()
    {
        var book = await AddUploadAsync("A");
        var added = await _service.AddAsync("ivan", book.Id, Cfi, "x", 1);

        var result = await _service.RemoveAsync("ivan", added.Value!.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(_context.Bookmarks);
    }

    [Fact]
    public async Task RemoveAsync_AnotherUsersBookmark_IsNotFoundAndStays()
    {
        var book = await AddUploadAsync("A");
        var added = await _service.AddAsync("maria", book.Id, Cfi, "x", 1);

        var result = await _service.RemoveAsync("ivan", added.Value!.Id);

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Single(_context.Bookmarks);
    }

    [Fact]
    public async Task RemoveAsync_UnknownBookmark_IsNotFound()
    {
        Assert.Equal(ServiceError.NotFound, (await _service.RemoveAsync("ivan", 404)).Error);
    }

    // ---------- Cascades ----------

    [Fact]
    public async Task DeletingBook_RemovesItsBookmarks()
    {
        var book = await AddUploadAsync("A");
        await _service.AddAsync("ivan", book.Id, Cfi, "x", 1);

        _context.Resources.Remove(book);
        await _context.SaveChangesAsync();

        Assert.Empty(_context.Bookmarks);
    }
}
