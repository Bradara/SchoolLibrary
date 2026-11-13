using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Reports;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

public class ReportServiceTests
{
    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly ReportService _service;

    private readonly ApplicationUser _ivan = new() { Id = "ivan", UserName = "ivan", FirstName = "Иван", LastName = "Петров" };
    private readonly ApplicationUser _maria = new() { Id = "maria", UserName = "maria", FirstName = "Мария", LastName = "Георгиева" };
    private readonly Resource _resource = new() { Title = "Дроби", Author = "Автор", Url = "https://example.com/a", OwnerId = "ivan", CreatedOn = DateTime.UtcNow };
    private readonly Comment _comment;

    public ReportServiceTests()
    {
        _context.Users.AddRange(_ivan, _maria);
        _context.Resources.Add(_resource);
        _context.SaveChanges();

        _comment = new Comment { ResourceId = _resource.Id, AuthorId = "ivan", Text = "Спорен коментар", CreatedOn = DateTime.UtcNow };
        _context.Comments.Add(_comment);
        _context.SaveChanges();

        _service = new ReportService(_context, new ResourceService(_context), new CommentService(_context));
    }

    private ReportInput ResourceReport(ReportReason reason = ReportReason.Copyright, string? details = null) =>
        new() { ResourceId = _resource.Id, Reason = reason, Details = details };

    private ReportInput CommentReport(ReportReason reason = ReportReason.Inappropriate, string? details = null) =>
        new() { CommentId = _comment.Id, Reason = reason, Details = details };

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_ResourceReport_SavesNewReportWithSnapshot()
    {
        var before = DateTime.UtcNow;

        var result = await _service.CreateAsync(ResourceReport(details: "  Копирано от учебник  "), "maria");

        Assert.True(result.Succeeded);
        var saved = Assert.Single(_context.Reports);
        Assert.Equal(ReportTarget.Resource, saved.Target);
        Assert.Equal(_resource.Id, saved.ResourceId);
        Assert.Null(saved.CommentId);
        Assert.Equal("Дроби", saved.TargetTitle);
        Assert.Null(saved.TargetText);
        Assert.Equal(ReportReason.Copyright, saved.Reason);
        Assert.Equal("Копирано от учебник", saved.Details);
        Assert.Equal(ReportStatus.New, saved.Status);
        Assert.Equal("maria", saved.ReporterId);
        Assert.True(saved.CreatedOn >= before);
        Assert.Null(saved.ReviewedOn);
    }

    [Fact]
    public async Task CreateAsync_CommentReport_KeepsCommentTextAndItsResource()
    {
        var result = await _service.CreateAsync(CommentReport(), "maria");

        Assert.True(result.Succeeded);
        var saved = _context.Reports.Single();
        Assert.Equal(ReportTarget.Comment, saved.Target);
        Assert.Equal(_comment.Id, saved.CommentId);
        Assert.Equal(_resource.Id, saved.ResourceId);
        Assert.Equal("Дроби", saved.TargetTitle);
        Assert.Equal("Спорен коментар", saved.TargetText);
    }

    [Fact]
    public async Task CreateAsync_WhitespaceDetails_AreStoredAsNull()
    {
        await _service.CreateAsync(ResourceReport(details: "   "), "maria");

        Assert.Null(_context.Reports.Single().Details);
    }

    [Fact]
    public async Task CreateAsync_NeitherTarget_IsInvalid()
    {
        var result = await _service.CreateAsync(new ReportInput { Reason = ReportReason.Copyright }, "maria");

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.Reports);
    }

    [Fact]
    public async Task CreateAsync_BothTargets_IsInvalid()
    {
        var input = new ReportInput { ResourceId = _resource.Id, CommentId = _comment.Id, Reason = ReportReason.Copyright };

        var result = await _service.CreateAsync(input, "maria");

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.Reports);
    }

    [Fact]
    public async Task CreateAsync_UnknownReason_IsInvalid()
    {
        var result = await _service.CreateAsync(ResourceReport((ReportReason)99), "maria");

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("причина", result.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_OtherReasonWithoutDetails_IsInvalid(string? details)
    {
        var result = await _service.CreateAsync(ResourceReport(ReportReason.Other, details), "maria");

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("Опишете", result.Message);
    }

    [Fact]
    public async Task CreateAsync_OtherReasonWithDetails_IsAccepted()
    {
        var result = await _service.CreateAsync(ResourceReport(ReportReason.Other, "Друго нарушение"), "maria");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CreateAsync_DetailsExactlyMaxLength_IsAccepted()
    {
        var result = await _service.CreateAsync(ResourceReport(details: new string('а', ReportService.DetailsMaxLength)), "maria");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CreateAsync_DetailsTooLong_IsInvalid()
    {
        var result = await _service.CreateAsync(ResourceReport(details: new string('а', ReportService.DetailsMaxLength + 1)), "maria");

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("500", result.Message);
    }

    [Fact]
    public async Task CreateAsync_DetailsLineBreaksCountAsOneCharacter()
    {
        var details = new string('а', 249) + "\r\n" + new string('б', 250);

        var result = await _service.CreateAsync(ResourceReport(details: details), "maria");

        Assert.True(result.Succeeded);
        Assert.Equal(500, _context.Reports.Single().Details!.Length);
    }

    [Fact]
    public async Task CreateAsync_UnknownResource_ReturnsNotFound()
    {
        var result = await _service.CreateAsync(new ReportInput { ResourceId = 999, Reason = ReportReason.Copyright }, "maria");

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Empty(_context.Reports);
    }

    [Fact]
    public async Task CreateAsync_UnknownComment_ReturnsNotFound()
    {
        var result = await _service.CreateAsync(new ReportInput { CommentId = 999, Reason = ReportReason.Inappropriate }, "maria");

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task CreateAsync_SameUserSameResourceWhileOpen_IsConflict()
    {
        await _service.CreateAsync(ResourceReport(), "maria");

        var second = await _service.CreateAsync(ResourceReport(ReportReason.BrokenLink), "maria");

        Assert.Equal(ServiceError.Conflict, second.Error);
        Assert.Single(_context.Reports);
    }

    [Fact]
    public async Task CreateAsync_SameUserSameCommentWhileOpen_IsConflict()
    {
        await _service.CreateAsync(CommentReport(), "maria");

        var second = await _service.CreateAsync(CommentReport(), "maria");

        Assert.Equal(ServiceError.Conflict, second.Error);
    }

    [Fact]
    public async Task CreateAsync_AnotherUserMayReportTheSameContent()
    {
        await _service.CreateAsync(ResourceReport(), "maria");

        var other = await _service.CreateAsync(ResourceReport(), "ivan");

        Assert.True(other.Succeeded);
        Assert.Equal(2, _context.Reports.Count());
    }

    [Fact]
    public async Task CreateAsync_ReportingTheResourceAndItsCommentAreTwoReports()
    {
        await _service.CreateAsync(ResourceReport(), "maria");

        var second = await _service.CreateAsync(CommentReport(), "maria");

        Assert.True(second.Succeeded);
    }

    [Theory]
    [InlineData(ReportStatus.Reviewed)]
    [InlineData(ReportStatus.Rejected)]
    public async Task CreateAsync_AfterTheFirstReportWasHandled_ReportingAgainIsAllowed(ReportStatus handled)
    {
        var first = await _service.CreateAsync(ResourceReport(), "maria");
        await _service.ResolveAsync(first.Value!.Id, handled);

        var again = await _service.CreateAsync(ResourceReport(), "maria");

        Assert.True(again.Succeeded);
    }

    // ---------- SearchAsync ----------

    private async Task AddReportAsync(ReportStatus status, ReportTarget target, DateTime createdOn)
    {
        _context.Reports.Add(new Report
        {
            Target = target,
            ResourceId = _resource.Id,
            CommentId = target == ReportTarget.Comment ? _comment.Id : null,
            TargetTitle = "Дроби",
            Reason = ReportReason.Copyright,
            Status = status,
            CreatedOn = createdOn,
            ReporterId = "maria"
        });
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsNewestFirst()
    {
        await AddReportAsync(ReportStatus.New, ReportTarget.Resource, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddReportAsync(ReportStatus.Reviewed, ReportTarget.Resource, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await _service.SearchAsync(new ReportFilter());

        Assert.Equal(new[] { ReportStatus.Reviewed, ReportStatus.New }, result.Items.Select(r => r.Status));
    }

    [Fact]
    public async Task SearchAsync_FiltersByStatusAndTarget()
    {
        await AddReportAsync(ReportStatus.New, ReportTarget.Resource, DateTime.UtcNow);
        await AddReportAsync(ReportStatus.New, ReportTarget.Comment, DateTime.UtcNow);
        await AddReportAsync(ReportStatus.Rejected, ReportTarget.Comment, DateTime.UtcNow);

        var newOnes = await _service.SearchAsync(new ReportFilter { Status = ReportStatus.New });
        var comments = await _service.SearchAsync(new ReportFilter { Target = ReportTarget.Comment });
        var newComments = await _service.SearchAsync(new ReportFilter { Status = ReportStatus.New, Target = ReportTarget.Comment });

        Assert.Equal(2, newOnes.TotalCount);
        Assert.Equal(2, comments.TotalCount);
        Assert.Equal(1, newComments.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_LoadsReporterAndCommentAuthor()
    {
        await AddReportAsync(ReportStatus.New, ReportTarget.Comment, DateTime.UtcNow);

        var report = (await _service.SearchAsync(new ReportFilter())).Items.Single();

        Assert.Equal("Мария Георгиева", report.Reporter!.FullName);
        Assert.Equal("Иван Петров", report.Comment!.Author.FullName);
    }

    [Fact]
    public async Task SearchAsync_PagesAndClampsThePage()
    {
        for (var i = 1; i <= 5; i++)
        {
            await AddReportAsync(ReportStatus.New, ReportTarget.Resource, new DateTime(2026, 1, i, 0, 0, 0, DateTimeKind.Utc));
        }

        var second = await _service.SearchAsync(new ReportFilter { Page = 2, PageSize = 2 });
        var tooFar = await _service.SearchAsync(new ReportFilter { Page = 99, PageSize = 2 });
        var tooLow = await _service.SearchAsync(new ReportFilter { Page = -1, PageSize = 2 });

        Assert.Equal(2, second.Items.Count);
        Assert.Equal(3, second.TotalPages);
        Assert.Equal(3, tooFar.Page);
        Assert.Equal(1, tooLow.Page);
    }

    [Fact]
    public async Task SearchAsync_Empty_ReturnsEmptyFirstPage()
    {
        var result = await _service.SearchAsync(new ReportFilter());

        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
    }

    // ---------- ResolveAsync ----------

    [Theory]
    [InlineData(ReportStatus.Reviewed)]
    [InlineData(ReportStatus.Rejected)]
    public async Task ResolveAsync_SetsStatusAndReviewTime(ReportStatus status)
    {
        var report = (await _service.CreateAsync(ResourceReport(), "maria")).Value!;
        var before = DateTime.UtcNow;

        var result = await _service.ResolveAsync(report.Id, status);

        Assert.True(result.Succeeded);
        var saved = _context.Reports.Single();
        Assert.Equal(status, saved.Status);
        Assert.True(saved.ReviewedOn >= before);
    }

    [Fact]
    public async Task ResolveAsync_CanChangeAHandledReport()
    {
        var report = (await _service.CreateAsync(ResourceReport(), "maria")).Value!;
        await _service.ResolveAsync(report.Id, ReportStatus.Rejected);

        var result = await _service.ResolveAsync(report.Id, ReportStatus.Reviewed);

        Assert.True(result.Succeeded);
        Assert.Equal(ReportStatus.Reviewed, _context.Reports.Single().Status);
    }

    [Fact]
    public async Task ResolveAsync_NewIsNotAValidResult()
    {
        var report = (await _service.CreateAsync(ResourceReport(), "maria")).Value!;

        var result = await _service.ResolveAsync(report.Id, ReportStatus.New);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal(ReportStatus.New, _context.Reports.Single().Status);
    }

    [Fact]
    public async Task ResolveAsync_UnknownReport_ReturnsNotFound()
    {
        var result = await _service.ResolveAsync(999, ReportStatus.Reviewed);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    // ---------- DeleteTargetAsync ----------

    [Fact]
    public async Task DeleteTargetAsync_Resource_DeletesItAndMarksTheReportReviewed()
    {
        var report = (await _service.CreateAsync(ResourceReport(), "maria")).Value!;

        var result = await _service.DeleteTargetAsync(report.Id, "admin");

        Assert.True(result.Succeeded);
        Assert.Empty(_context.Resources);
        var saved = _context.Reports.Single();
        Assert.Equal(ReportStatus.Reviewed, saved.Status);
        Assert.NotNull(saved.ReviewedOn);
        Assert.Equal("Дроби", saved.TargetTitle);
    }

    [Fact]
    public async Task DeleteTargetAsync_Resource_SettlesEveryOpenReportAboutIt()
    {
        var first = (await _service.CreateAsync(ResourceReport(), "maria")).Value!;
        await _service.CreateAsync(ResourceReport(), "ivan");
        await _service.CreateAsync(CommentReport(), "maria");

        await _service.DeleteTargetAsync(first.Id, "admin");

        Assert.All(_context.Reports, r => Assert.Equal(ReportStatus.Reviewed, r.Status));
    }

    [Fact]
    public async Task DeleteTargetAsync_Resource_KeepsReportsAlreadyRejected()
    {
        var rejected = (await _service.CreateAsync(ResourceReport(), "ivan")).Value!;
        await _service.ResolveAsync(rejected.Id, ReportStatus.Rejected);
        var open = (await _service.CreateAsync(ResourceReport(), "maria")).Value!;

        await _service.DeleteTargetAsync(open.Id, "admin");

        Assert.Equal(ReportStatus.Rejected, _context.Reports.Single(r => r.Id == rejected.Id).Status);
        Assert.Equal(ReportStatus.Reviewed, _context.Reports.Single(r => r.Id == open.Id).Status);
    }

    [Fact]
    public async Task DeleteTargetAsync_Comment_DeletesOnlyTheCommentAndKeepsTheResource()
    {
        var report = (await _service.CreateAsync(CommentReport(), "maria")).Value!;

        var result = await _service.DeleteTargetAsync(report.Id, "admin");

        Assert.True(result.Succeeded);
        Assert.Empty(_context.Comments);
        Assert.Single(_context.Resources);
        var saved = _context.Reports.Single();
        Assert.Equal(ReportStatus.Reviewed, saved.Status);
        Assert.Equal("Спорен коментар", saved.TargetText);
    }

    [Fact]
    public async Task DeleteTargetAsync_Comment_DoesNotSettleReportsAboutTheResource()
    {
        var resourceReport = (await _service.CreateAsync(ResourceReport(), "maria")).Value!;
        var commentReport = (await _service.CreateAsync(CommentReport(), "maria")).Value!;

        await _service.DeleteTargetAsync(commentReport.Id, "admin");

        Assert.Equal(ReportStatus.New, _context.Reports.Single(r => r.Id == resourceReport.Id).Status);
    }

    [Fact]
    public async Task DeleteTargetAsync_ContentAlreadyDeleted_ReturnsConflict()
    {
        var report = (await _service.CreateAsync(ResourceReport(), "maria")).Value!;
        report = _context.Reports.Single();
        report.ResourceId = null; // what the database does when the resource is deleted
        await _context.SaveChangesAsync();

        var result = await _service.DeleteTargetAsync(report.Id, "admin");

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Contains("вече е изтрит", result.Message);
    }

    [Fact]
    public async Task DeleteTargetAsync_CommentAlreadyDeleted_ReturnsConflict()
    {
        var report = (await _service.CreateAsync(CommentReport(), "maria")).Value!;
        report = _context.Reports.Single();
        report.CommentId = null;
        await _context.SaveChangesAsync();

        var result = await _service.DeleteTargetAsync(report.Id, "admin");

        Assert.Equal(ServiceError.Conflict, result.Error);
    }

    [Fact]
    public async Task DeleteTargetAsync_UnknownReport_ReturnsNotFound()
    {
        var result = await _service.DeleteTargetAsync(999, "admin");

        Assert.Equal(ServiceError.NotFound, result.Error);
    }
}
