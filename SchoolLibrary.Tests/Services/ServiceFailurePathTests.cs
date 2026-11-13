using Moq;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Reports;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

// What the services do when a service they depend on refuses: the situations that cannot be made with the
// real services, such as content that another administrator deleted a moment earlier.
public class ServiceFailurePathTests
{
    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();

    // ---------- import: a row that passed the preview but is refused when it is saved ----------

    private async Task<ResourceImportService> ImportServiceThatRefusesAsync(ServiceError error, string message)
    {
        _context.Categories.Add(new Category { Name = "Математика" });
        _context.Licenses.Add(new License { Name = "CC BY 4.0" });
        _context.GradeLevels.Add(new GradeLevel { Number = 5 });
        await _context.SaveChangesAsync();

        var resources = new Mock<IResourceService>();
        resources.Setup(r => r.CreateAsync(It.IsAny<ResourceInput>(), null))
            .ReturnsAsync(ServiceResult<Resource>.Failure(error, message));

        return new ResourceImportService(_context, resources.Object);
    }

    private const string OneRow =
        "title;author;url;license;subjects;grades\nДроби;Автор;https://example.com/a;CC BY 4.0;Математика;5";

    [Fact]
    public async Task ImportAsync_ARowAddedByAnotherUserMeanwhile_IsReportedAsADuplicate()
    {
        var service = await ImportServiceThatRefusesAsync(ServiceError.Conflict, "Този материал вече съществува.");

        var result = await service.ImportAsync(OneRow);

        var row = Assert.Single(result.Rows);
        Assert.Equal(ResourceImportRowStatus.Duplicate, row.Status);
        Assert.Equal("Този материал вече съществува.", row.Message);
        Assert.Equal(0, result.ImportedCount);
    }

    [Fact]
    public async Task ImportAsync_ARowTheServiceRefusesForAnotherReason_IsReportedAsInvalid()
    {
        var service = await ImportServiceThatRefusesAsync(ServiceError.Invalid, "Избран е несъществуващ предмет.");

        var result = await service.ImportAsync(OneRow);

        var row = Assert.Single(result.Rows);
        Assert.Equal(ResourceImportRowStatus.Invalid, row.Status);
        Assert.Equal("Избран е несъществуващ предмет.", row.Message);
    }

    // ---------- reports: deleting the content fails ----------

    private async Task<(Report Report, Mock<IResourceService> Resources, Mock<ICommentService> Comments)> ReportAboutAsync(ReportTarget target)
    {
        var resource = new Resource { Title = "Дроби", Author = "А", Url = "https://example.com/a", CreatedOn = DateTime.UtcNow };
        _context.Resources.Add(resource);
        await _context.SaveChangesAsync();

        var comment = new Comment { ResourceId = resource.Id, AuthorId = "u", Text = "текст", CreatedOn = DateTime.UtcNow };
        _context.Users.Add(new ApplicationUser { Id = "u", UserName = "u", FirstName = "А", LastName = "Б" });
        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        var report = new Report
        {
            Target = target,
            ResourceId = resource.Id,
            CommentId = target == ReportTarget.Comment ? comment.Id : null,
            TargetTitle = "Дроби",
            Reason = ReportReason.Copyright,
            Status = ReportStatus.New,
            CreatedOn = DateTime.UtcNow
        };
        _context.Reports.Add(report);
        await _context.SaveChangesAsync();

        return (report, new Mock<IResourceService>(), new Mock<ICommentService>());
    }

    [Fact]
    public async Task DeleteTargetAsync_TheResourceCannotBeDeleted_FailsAndLeavesTheReportOpen()
    {
        var (report, resources, comments) = await ReportAboutAsync(ReportTarget.Resource);
        resources.Setup(r => r.DeleteAsync(It.IsAny<int>(), "admin", true))
            .ReturnsAsync(ServiceResult<Resource>.Failure(ServiceError.NotFound, "Ресурсът не е намерен."));
        var service = new ReportService(_context, resources.Object, comments.Object);

        var result = await service.DeleteTargetAsync(report.Id, "admin");

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Equal("Ресурсът не е намерен.", result.Message);
        Assert.Equal(ReportStatus.New, _context.Reports.Single().Status);
    }

    [Fact]
    public async Task DeleteTargetAsync_TheCommentCannotBeDeleted_FailsAndLeavesTheReportOpen()
    {
        var (report, resources, comments) = await ReportAboutAsync(ReportTarget.Comment);
        comments.Setup(c => c.DeleteAsync(It.IsAny<int>(), "admin", true))
            .ReturnsAsync(ServiceResult<Comment>.Failure(ServiceError.NotFound, "Коментарът не е намерен."));
        var service = new ReportService(_context, resources.Object, comments.Object);

        var result = await service.DeleteTargetAsync(report.Id, "admin");

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Equal(ReportStatus.New, _context.Reports.Single().Status);
    }

    [Fact]
    public async Task DeleteTargetAsync_DeletesAsAnAdministrator_WhoeverTheAuthorIs()
    {
        var (report, resources, comments) = await ReportAboutAsync(ReportTarget.Resource);
        resources.Setup(r => r.DeleteAsync(It.IsAny<int>(), "admin-1", true))
            .ReturnsAsync(ServiceResult<Resource>.Success(new Resource()));
        var service = new ReportService(_context, resources.Object, comments.Object);

        var result = await service.DeleteTargetAsync(report.Id, "admin-1");

        Assert.True(result.Succeeded);
        resources.Verify(r => r.DeleteAsync(report.ResourceId!.Value, "admin-1", true), Times.Once);
        comments.VerifyNoOtherCalls();
    }
}
