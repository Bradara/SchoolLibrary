using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Tests.Services;

public class CommentServiceTests
{
    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly CommentService _service;

    private readonly ApplicationUser _ivan = new() { Id = "ivan", UserName = "ivan", FirstName = "Иван", LastName = "Петров" };
    private readonly ApplicationUser _maria = new() { Id = "maria", UserName = "maria", FirstName = "Мария", LastName = "Георгиева" };
    private readonly Resource _resource = new() { Title = "Дроби", Author = "Автор", Url = "https://example.com/a", OwnerId = "ivan", CreatedOn = DateTime.UtcNow };

    public CommentServiceTests()
    {
        _service = new CommentService(_context);
        _context.Users.AddRange(_ivan, _maria);
        _context.Resources.Add(_resource);
        _context.SaveChanges();
    }

    private async Task<Comment> SaveCommentAsync(string authorId, string text, DateTime? createdOn = null, int? resourceId = null)
    {
        var comment = new Comment
        {
            ResourceId = resourceId ?? _resource.Id,
            AuthorId = authorId,
            Text = text,
            CreatedOn = createdOn ?? DateTime.UtcNow
        };
        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();
        return comment;
    }

    // ---------- AddAsync ----------

    [Fact]
    public async Task AddAsync_ValidText_SavesCommentWithAuthorAndTime()
    {
        var before = DateTime.UtcNow;

        var result = await _service.AddAsync(_resource.Id, "maria", "Много полезно, благодаря!");

        Assert.True(result.Succeeded);
        var saved = Assert.Single(_context.Comments);
        Assert.Equal("Много полезно, благодаря!", saved.Text);
        Assert.Equal("maria", saved.AuthorId);
        Assert.Equal(_resource.Id, saved.ResourceId);
        Assert.True(saved.CreatedOn >= before);
    }

    [Fact]
    public async Task AddAsync_TrimsTheText()
    {
        await _service.AddAsync(_resource.Id, "maria", "  Здравейте  \n");

        Assert.Equal("Здравейте", _context.Comments.Single().Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\r\n\t")]
    public async Task AddAsync_EmptyText_IsInvalidAndSavesNothing(string text)
    {
        var result = await _service.AddAsync(_resource.Id, "maria", text);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("Напишете коментар", result.Message);
        Assert.Empty(_context.Comments);
    }

    [Fact]
    public async Task AddAsync_NullText_IsInvalid()
    {
        var result = await _service.AddAsync(_resource.Id, "maria", null!);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task AddAsync_ExactlyMaxLength_IsAccepted()
    {
        var result = await _service.AddAsync(_resource.Id, "maria", new string('а', CommentService.TextMaxLength));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AddAsync_OneCharacterTooLong_IsInvalid()
    {
        var result = await _service.AddAsync(_resource.Id, "maria", new string('а', CommentService.TextMaxLength + 1));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("500", result.Message);
        Assert.Empty(_context.Comments);
    }

    [Fact]
    public async Task AddAsync_LineBreaksFromTheBrowserCountAsOneCharacter()
    {
        // 249 + CRLF + 250 is 500 characters in the textarea, but 501 once the browser sends CRLF
        var text = new string('а', 249) + "\r\n" + new string('б', 250);

        var result = await _service.AddAsync(_resource.Id, "maria", text);

        Assert.True(result.Succeeded);
        Assert.Equal(500, _context.Comments.Single().Text.Length);
        Assert.DoesNotContain('\r', _context.Comments.Single().Text);
    }

    [Fact]
    public async Task AddAsync_UnknownResource_ReturnsNotFoundAndSavesNothing()
    {
        var result = await _service.AddAsync(999, "maria", "Здравейте");

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Empty(_context.Comments);
    }

    [Fact]
    public async Task AddAsync_HtmlIsStoredAsTyped_EscapingIsLeftToTheView()
    {
        await _service.AddAsync(_resource.Id, "maria", "<script>alert(1)</script>");

        Assert.Equal("<script>alert(1)</script>", _context.Comments.Single().Text);
    }

    // ---------- GetForResourceAsync ----------

    [Fact]
    public async Task GetForResourceAsync_ReturnsOnlyThisResourcesCommentsNewestFirst()
    {
        var other = new Resource { Title = "Друг", Author = "А", Url = "https://example.com/b", CreatedOn = DateTime.UtcNow };
        _context.Resources.Add(other);
        await _context.SaveChangesAsync();
        await SaveCommentAsync("ivan", "стар", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await SaveCommentAsync("maria", "нов", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        await SaveCommentAsync("maria", "чужд", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), other.Id);

        var result = await _service.GetForResourceAsync(_resource.Id, 1);

        Assert.Equal(new[] { "нов", "стар" }, result.Items.Select(c => c.Text));
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task GetForResourceAsync_LoadsTheAuthor()
    {
        await SaveCommentAsync("maria", "Здравейте");

        var comment = (await _service.GetForResourceAsync(_resource.Id, 1)).Items.Single();

        Assert.Equal("Мария Георгиева", comment.Author.FullName);
    }

    [Fact]
    public async Task GetForResourceAsync_SameTime_OrderIsStableByNewestId()
    {
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await SaveCommentAsync("ivan", "първи", time);
        await SaveCommentAsync("ivan", "втори", time);

        var result = await _service.GetForResourceAsync(_resource.Id, 1);

        Assert.Equal(new[] { "втори", "първи" }, result.Items.Select(c => c.Text));
    }

    [Fact]
    public async Task GetForResourceAsync_PagesTheResults()
    {
        for (var i = 1; i <= 5; i++)
        {
            await SaveCommentAsync("ivan", "К" + i, new DateTime(2026, 1, i, 0, 0, 0, DateTimeKind.Utc));
        }

        var second = await _service.GetForResourceAsync(_resource.Id, page: 2, pageSize: 2);

        Assert.Equal(new[] { "К3", "К2" }, second.Items.Select(c => c.Text));
        Assert.Equal(5, second.TotalCount);
        Assert.Equal(3, second.TotalPages);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(50, 2)]
    public async Task GetForResourceAsync_PageOutOfRange_IsClamped(int requested, int expected)
    {
        for (var i = 1; i <= 3; i++)
        {
            await SaveCommentAsync("ivan", "К" + i);
        }

        var result = await _service.GetForResourceAsync(_resource.Id, requested, pageSize: 2);

        Assert.Equal(expected, result.Page);
    }

    [Fact]
    public async Task GetForResourceAsync_NoComments_ReturnsEmptyFirstPage()
    {
        var result = await _service.GetForResourceAsync(_resource.Id, 1);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.Page);
    }

    // ---------- DeleteAsync ----------

    [Fact]
    public async Task DeleteAsync_Author_DeletesOwnComment()
    {
        var comment = await SaveCommentAsync("maria", "Здравейте");

        var result = await _service.DeleteAsync(comment.Id, "maria", isAdministrator: false);

        Assert.True(result.Succeeded);
        Assert.Equal(_resource.Id, result.Value!.ResourceId);
        Assert.Empty(_context.Comments);
    }

    [Fact]
    public async Task DeleteAsync_Administrator_DeletesAnyComment()
    {
        var comment = await SaveCommentAsync("maria", "Здравейте");

        var result = await _service.DeleteAsync(comment.Id, "admin", isAdministrator: true);

        Assert.True(result.Succeeded);
        Assert.Empty(_context.Comments);
    }

    [Fact]
    public async Task DeleteAsync_AnotherUser_IsForbiddenAndKeepsTheComment()
    {
        var comment = await SaveCommentAsync("maria", "Здравейте");

        var result = await _service.DeleteAsync(comment.Id, "ivan", isAdministrator: false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
        Assert.Single(_context.Comments);
    }

    [Fact]
    public async Task DeleteAsync_OwnerOfTheResource_CannotDeleteSomeoneElsesComment()
    {
        // "ivan" owns the resource but did not write the comment
        var comment = await SaveCommentAsync("maria", "Здравейте");

        var result = await _service.DeleteAsync(comment.Id, "ivan", isAdministrator: false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task DeleteAsync_UnknownComment_ReturnsNotFound()
    {
        var result = await _service.DeleteAsync(999, "maria", isAdministrator: false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    // ---------- CommentPermissions ----------

    [Theory]
    [InlineData("maria", "maria", false, true)]
    [InlineData("maria", "maria", true, true)]
    [InlineData("maria", "ivan", false, false)]
    [InlineData("maria", "ivan", true, true)]
    [InlineData("maria", null, false, false)]
    [InlineData("maria", null, true, true)]
    public void CanDelete_AuthorOrAdministrator(string authorId, string? userId, bool isAdministrator, bool expected)
    {
        var comment = new Comment { AuthorId = authorId };

        Assert.Equal(expected, CommentPermissions.CanDelete(comment, userId, isAdministrator));
    }
}
