using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Reading;

namespace SchoolLibrary.Tests.Services;

public class HighlightServiceTests
{
    private const string Cfi = "epubcfi(/6/4!/4/2,/1:0,/1:25)";

    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly HighlightService _service;

    public HighlightServiceTests()
    {
        _service = new HighlightService(_context);
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

    private static HighlightInput Input(string? cfi = Cfi, string? text = "Беше тъмна нощ", string? color = "green",
        string? note = null, int percent = 10) => new(cfi, text, color, note, percent);

    // ---------- AddAsync ----------

    [Fact]
    public async Task AddAsync_SavesHighlight()
    {
        var book = await AddUploadAsync("A");

        var result = await _service.AddAsync("ivan", book.Id, Input(note: "Важно за урока"));

        Assert.True(result.Succeeded);
        var saved = Assert.Single(_context.Highlights);
        Assert.Equal(saved.Id, result.Value!.Id);
        Assert.Equal("ivan", saved.UserId);
        Assert.Equal(Cfi, saved.Cfi);
        Assert.Equal("Беше тъмна нощ", saved.Text);
        Assert.Equal("green", saved.Color);
        Assert.Equal("Важно за урока", saved.Note);
        Assert.Equal(10, saved.Percent);
        Assert.Equal(saved.CreatedOn, saved.UpdatedOn);
    }

    [Fact]
    public async Task AddAsync_TidiesTheTextIntoOneLine()
    {
        var book = await AddUploadAsync("A");

        await _service.AddAsync("ivan", book.Id, Input(text: "  Беше\n\n тъмна\tнощ  "));

        Assert.Equal("Беше тъмна нощ", _context.Highlights.Single().Text);
    }

    [Fact]
    public async Task AddAsync_CutsVeryLongText()
    {
        var book = await AddUploadAsync("A");

        await _service.AddAsync("ivan", book.Id, Input(text: new string('а', Highlight.TextMaxLength * 3)));

        var text = _context.Highlights.Single().Text;
        Assert.Equal(Highlight.TextMaxLength, text.Length);
        Assert.EndsWith("…", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public async Task AddAsync_RefusesEmptyText(string? text)
    {
        var book = await AddUploadAsync("A");

        var result = await _service.AddAsync("ivan", book.Id, Input(text: text));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.Highlights);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/6/4!/4")]
    [InlineData("javascript:alert(1)")]
    public async Task AddAsync_RefusesInvalidCfi(string? cfi)
    {
        var book = await AddUploadAsync("A");

        Assert.Equal(ServiceError.Invalid, (await _service.AddAsync("ivan", book.Id, Input(cfi: cfi))).Error);
        Assert.Empty(_context.Highlights);
    }

    [Theory]
    [InlineData(null, "yellow")]
    [InlineData("", "yellow")]
    [InlineData("red", "yellow")]
    [InlineData("url(javascript:x)", "yellow")]
    [InlineData(" BLUE ", "blue")]
    [InlineData("pink", "pink")]
    public async Task AddAsync_AcceptsOnlyTheReadersColors(string? color, string expected)
    {
        var book = await AddUploadAsync("A");

        await _service.AddAsync("ivan", book.Id, Input(color: color));

        Assert.Equal(expected, _context.Highlights.Single().Color);
    }

    [Fact]
    public async Task AddAsync_RefusesTooLongNote()
    {
        var book = await AddUploadAsync("A");

        var result = await _service.AddAsync("ivan", book.Id, Input(note: new string('б', Highlight.NoteMaxLength + 1)));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.Highlights);
    }

    [Fact]
    public async Task AddAsync_RefusesResourceThatIsNotABook()
    {
        var pdf = await AddUploadAsync("A", "application/pdf");

        Assert.Equal(ServiceError.NotFound, (await _service.AddAsync("ivan", pdf.Id, Input())).Error);
        Assert.Equal(ServiceError.NotFound, (await _service.AddAsync("ivan", 999, Input())).Error);
    }

    [Fact]
    public async Task AddAsync_SamePassageAgain_ReturnsExistingHighlight()
    {
        var book = await AddUploadAsync("A");
        var first = await _service.AddAsync("ivan", book.Id, Input(color: "green"));

        var second = await _service.AddAsync("ivan", book.Id, Input(color: "pink"));

        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Equal("green", second.Value.Color);
        Assert.Single(_context.Highlights);
    }

    [Fact]
    public async Task AddAsync_RefusesMoreThanTheLimitPerBook()
    {
        var book = await AddUploadAsync("A");
        for (var i = 0; i < HighlightService.MaxPerBook; i++)
        {
            _context.Highlights.Add(new Highlight
            {
                UserId = "ivan", ResourceId = book.Id, Cfi = $"epubcfi(/6/{i})", Text = "x", Color = "yellow",
                CreatedOn = DateTime.UtcNow, UpdatedOn = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();

        Assert.Equal(ServiceError.Conflict, (await _service.AddAsync("ivan", book.Id, Input())).Error);
        Assert.True((await _service.AddAsync("maria", book.Id, Input())).Succeeded);
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task UpdateAsync_ChangesColorAndNote()
    {
        var book = await AddUploadAsync("A");
        var added = await _service.AddAsync("ivan", book.Id, Input());

        var result = await _service.UpdateAsync("ivan", added.Value!.Id, "blue", "Ред 1\r\nРед 2");

        Assert.True(result.Succeeded);
        var saved = _context.Highlights.Single();
        Assert.Equal("blue", saved.Color);
        Assert.Equal("Ред 1\nРед 2", saved.Note);
        Assert.True(saved.UpdatedOn >= saved.CreatedOn);
    }

    [Fact]
    public async Task UpdateAsync_EmptyNote_RemovesTheNote()
    {
        var book = await AddUploadAsync("A");
        var added = await _service.AddAsync("ivan", book.Id, Input(note: "бележка"));

        await _service.UpdateAsync("ivan", added.Value!.Id, "green", "   ");

        Assert.Null(_context.Highlights.Single().Note);
    }

    [Fact]
    public async Task UpdateAsync_WithoutColor_KeepsTheColor()
    {
        var book = await AddUploadAsync("A");
        var added = await _service.AddAsync("ivan", book.Id, Input(color: "pink"));

        await _service.UpdateAsync("ivan", added.Value!.Id, null, "бележка");

        Assert.Equal("pink", _context.Highlights.Single().Color);
    }

    [Fact]
    public async Task UpdateAsync_AnotherUsersHighlight_IsNotFoundAndUnchanged()
    {
        var book = await AddUploadAsync("A");
        var added = await _service.AddAsync("maria", book.Id, Input(note: "моя"));

        var result = await _service.UpdateAsync("ivan", added.Value!.Id, "blue", "чужда");

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Equal("моя", _context.Highlights.Single().Note);
    }

    [Fact]
    public async Task UpdateAsync_TooLongNote_IsRefused()
    {
        var book = await AddUploadAsync("A");
        var added = await _service.AddAsync("ivan", book.Id, Input(note: "стара"));

        var result = await _service.UpdateAsync("ivan", added.Value!.Id, "green", new string('б', Highlight.NoteMaxLength + 1));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("стара", _context.Highlights.Single().Note);
    }

    // ---------- the note ----------

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" текст ", "текст")]
    [InlineData("а\r\nб\rв", "а\nб\nв")]
    [InlineData("а\tб", "а\tб")]
    public void TryCleanNote_KeepsLinesAndTrims(string? note, string? expected)
    {
        Assert.True(HighlightService.TryCleanNote(note, out var clean, out _));
        Assert.Equal(expected, clean);
    }

    [Fact]
    public void TryCleanNote_RefusesControlCharacters()
    {
        Assert.False(HighlightService.TryCleanNote("а\u0007б", out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryCleanNote_AcceptsNoteOfMaxLength()
    {
        Assert.True(HighlightService.TryCleanNote(new string('б', Highlight.NoteMaxLength), out var clean, out _));
        Assert.Equal(Highlight.NoteMaxLength, clean!.Length);
    }

    // ---------- GetAsync / RemoveAsync ----------

    [Fact]
    public async Task GetAsync_ReturnsOnlyTheUsersHighlightsOfTheBookInReadingOrder()
    {
        var book = await AddUploadAsync("A");
        var other = await AddUploadAsync("B");
        await _service.AddAsync("ivan", book.Id, Input(cfi: "epubcfi(/6/8)", text: "Края", percent: 90));
        await _service.AddAsync("ivan", book.Id, Input(cfi: "epubcfi(/6/2)", text: "Начало", percent: 5));
        await _service.AddAsync("ivan", other.Id, Input(cfi: "epubcfi(/6/4)", text: "Друга книга"));
        await _service.AddAsync("maria", book.Id, Input(cfi: "epubcfi(/6/6)", text: "На Мария"));

        var list = await _service.GetAsync("ivan", book.Id);

        Assert.Equal(new[] { "Начало", "Края" }, list.Select(h => h.Text));
    }

    [Fact]
    public async Task RemoveAsync_RemovesOwnHighlightOnly()
    {
        var book = await AddUploadAsync("A");
        var mine = await _service.AddAsync("ivan", book.Id, Input());
        var hers = await _service.AddAsync("maria", book.Id, Input());

        Assert.Equal(ServiceError.NotFound, (await _service.RemoveAsync("ivan", hers.Value!.Id)).Error);
        Assert.True((await _service.RemoveAsync("ivan", mine.Value!.Id)).Succeeded);

        var left = Assert.Single(_context.Highlights);
        Assert.Equal("maria", left.UserId);
    }

    [Fact]
    public async Task DeletingBook_RemovesItsHighlights()
    {
        var book = await AddUploadAsync("A");
        await _service.AddAsync("ivan", book.Id, Input());

        _context.Resources.Remove(book);
        await _context.SaveChangesAsync();

        Assert.Empty(_context.Highlights);
    }
}

public class ReaderTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" \n\t ", "")]
    [InlineData("  а  б  ", "а б")]
    [InlineData("а\u0000б", "а б")]
    public void OneLine_CollapsesSpaces(string? text, string expected)
    {
        Assert.Equal(expected, ReaderText.OneLine(text, 100));
    }

    [Fact]
    public void OneLine_CutsWithAnEllipsisOnlyWhenLonger()
    {
        Assert.Equal("абв", ReaderText.OneLine("абв", 3));
        Assert.Equal("аб…", ReaderText.OneLine("абвг", 3));
    }
}
