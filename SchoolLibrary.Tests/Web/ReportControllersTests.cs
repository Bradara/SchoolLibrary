using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Areas.Admin.Models;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Reports;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.ViewModels;
using AdminReportsController = SchoolLibrary.Areas.Admin.Controllers.ReportsController;
using PublicReportsController = SchoolLibrary.Controllers.ReportsController;

namespace SchoolLibrary.Tests.Web;

// The page where a user reports a resource or a comment
public class PublicReportsControllerTests
{
    private readonly Mock<IReportService> _reports = new();
    private readonly Mock<IResourceService> _resources = new();
    private readonly Mock<ICommentService> _comments = new();

    private PublicReportsController Controller() =>
        new PublicReportsController(_reports.Object, _resources.Object, _comments.Object).WithUser("u1");

    private void ResourceExists() =>
        _resources.Setup(r => r.GetDetailsAsync(5)).ReturnsAsync(new Resource { Id = 5, Title = "Дроби" });

    private void CommentExists() =>
        _comments.Setup(c => c.GetAsync(7)).ReturnsAsync(new Comment
        {
            Id = 7,
            ResourceId = 5,
            Text = "Спорен коментар",
            Resource = new Resource { Id = 5, Title = "Дроби" },
            Author = new ApplicationUser { FirstName = "Мария", LastName = "Георгиева" }
        });

    // ---------- the form ----------

    [Fact]
    public async Task Create_ForAResource_ShowsItsTitle()
    {
        ResourceExists();

        var result = await Controller().Create(resourceId: 5, commentId: null);

        var model = Assert.IsType<ReportFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Дроби", model.TargetTitle);
        Assert.Equal(5, model.BackResourceId);
        Assert.False(model.IsComment);
    }

    [Fact]
    public async Task Create_ForAComment_ShowsItsTextAuthorAndTheResourceToGoBackTo()
    {
        CommentExists();

        var result = await Controller().Create(resourceId: null, commentId: 7);

        var model = Assert.IsType<ReportFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.IsComment);
        Assert.Equal("Спорен коментар", model.TargetText);
        Assert.Equal("Мария Георгиева", model.TargetAuthor);
        Assert.Equal(5, model.BackResourceId);
        Assert.Equal("Дроби", model.TargetTitle);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(5, 7)]
    public async Task Create_WhenItIsNotClearWhatIsReported_ReturnsNotFound(int? resourceId, int? commentId)
    {
        ResourceExists();
        CommentExists();

        Assert.IsType<NotFoundResult>(await Controller().Create(resourceId, commentId));
    }

    [Fact]
    public async Task Create_UnknownResourceOrComment_ReturnsNotFound()
    {
        Assert.IsType<NotFoundResult>(await Controller().Create(resourceId: 99, commentId: null));
        Assert.IsType<NotFoundResult>(await Controller().Create(resourceId: null, commentId: 99));
    }

    // ---------- sending ----------

    [Fact]
    public async Task Create_Post_ResourceReport_IsSentAndTheUserGoesBackToTheResource()
    {
        ResourceExists();
        _reports.Setup(r => r.CreateAsync(It.Is<ReportInput>(i =>
                i.ResourceId == 5 && i.CommentId == null && i.Reason == ReportReason.Copyright && i.Details == "Копирано"), "u1"))
            .ReturnsAsync(ServiceResult<Report>.Success(new Report { ResourceId = 5 }));
        var controller = Controller();

        var result = await controller.Create(new ReportFormViewModel { ResourceId = 5, Reason = ReportReason.Copyright, Details = "Копирано" });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("Resources", redirect.ControllerName);
        Assert.Equal(5, redirect.RouteValues!["id"]);
        Assert.Null(redirect.Fragment);
        Assert.False(string.IsNullOrEmpty(controller.TempDataText("SuccessMessage")));
    }

    [Fact]
    public async Task Create_Post_CommentReport_GoesBackToTheCommentsOfTheResource()
    {
        CommentExists();
        _reports.Setup(r => r.CreateAsync(It.Is<ReportInput>(i => i.CommentId == 7 && i.ResourceId == null), "u1"))
            .ReturnsAsync(ServiceResult<Report>.Success(new Report { ResourceId = 5 }));

        var result = await Controller().Create(new ReportFormViewModel { CommentId = 7, Reason = ReportReason.Inappropriate });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(5, redirect.RouteValues!["id"]);
        Assert.Equal("comments", redirect.Fragment);
    }

    [Fact]
    public async Task Create_Post_InvalidForm_ShowsTheFormAgainWithoutSending()
    {
        ResourceExists();
        var controller = Controller();
        controller.ModelState.AddModelError("Reason", "Изберете причина");

        var result = await controller.Create(new ReportFormViewModel { ResourceId = 5 });

        var model = Assert.IsType<ReportFormViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Дроби", model.TargetTitle);
        _reports.Verify(r => r.CreateAsync(It.IsAny<ReportInput>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Create_Post_ASecondReportForTheSameContent_ShowsTheMessageOnTheForm()
    {
        ResourceExists();
        _reports.Setup(r => r.CreateAsync(It.IsAny<ReportInput>(), "u1"))
            .ReturnsAsync(ServiceResult<Report>.Failure(ServiceError.Conflict, "Вече сте подали сигнал."));
        var controller = Controller();

        var result = await controller.Create(new ReportFormViewModel { ResourceId = 5, Reason = ReportReason.BrokenLink });

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, e => e.ErrorMessage == "Вече сте подали сигнал.");
        Assert.Null(controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Create_Post_ContentDeletedMeanwhile_ReturnsNotFound()
    {
        ResourceExists();
        _reports.Setup(r => r.CreateAsync(It.IsAny<ReportInput>(), "u1"))
            .ReturnsAsync(ServiceResult<Report>.Failure(ServiceError.NotFound, "Ресурсът не е намерен."));

        var result = await Controller().Create(new ReportFormViewModel { ResourceId = 5, Reason = ReportReason.Copyright });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Create_Post_OfAnUnknownResource_ReturnsNotFoundWithoutSending()
    {
        var result = await Controller().Create(new ReportFormViewModel { ResourceId = 99, Reason = ReportReason.Copyright });

        Assert.IsType<NotFoundResult>(result);
        _reports.Verify(r => r.CreateAsync(It.IsAny<ReportInput>(), It.IsAny<string>()), Times.Never);
    }
}

// The moderation page of the administrators
public class AdminReportsControllerTests
{
    private readonly Mock<IReportService> _service = new();

    private AdminReportsController Controller() =>
        new AdminReportsController(_service.Object).WithUser("admin", Roles.Administrator);

    private ReportFilter? _filter;

    private void SearchReturnsNothing() =>
        _service.Setup(s => s.SearchAsync(It.IsAny<ReportFilter>()))
            .Callback<ReportFilter>(f => _filter = f)
            .ReturnsAsync(new PagedResult<Report>(new List<Report>(), 25, 2, 10));

    // ---------- the list ----------

    [Fact]
    public async Task Index_WithoutAStatus_OpensOnTheNewReports()
    {
        SearchReturnsNothing();

        var result = await Controller().Index(status: null, target: null);

        Assert.Equal(ReportStatus.New, _filter!.Status);
        Assert.Equal(ReportStatus.New, Assert.IsType<ReportIndexViewModel>(Assert.IsType<ViewResult>(result).Model).Status);
    }

    [Theory]
    [InlineData("New", ReportStatus.New)]
    [InlineData("Reviewed", ReportStatus.Reviewed)]
    [InlineData("rejected", ReportStatus.Rejected)]
    public async Task Index_WithAStatus_FiltersByIt(string status, ReportStatus expected)
    {
        SearchReturnsNothing();

        await Controller().Index(status, target: null);

        Assert.Equal(expected, _filter!.Status);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("garbage")]
    [InlineData("99")]
    [InlineData("-1")]
    public async Task Index_AllOrAnUnknownStatus_ShowsEveryReport(string status)
    {
        SearchReturnsNothing();

        var result = await Controller().Index(status, target: null);

        Assert.Null(_filter!.Status);
        var model = Assert.IsType<ReportIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Null(model.Status);
        Assert.Equal("all", model.StatusParameter);
    }

    [Fact]
    public async Task Index_PassesTheTargetAndThePage_AndShowsThePagingOfTheService()
    {
        SearchReturnsNothing();

        var result = await Controller().Index("all", ReportTarget.Comment, page: 2);

        Assert.Equal(ReportTarget.Comment, _filter!.Target);
        Assert.Equal(2, _filter.Page);
        var model = Assert.IsType<ReportIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(3, model.TotalPages);
        Assert.Equal(25, model.TotalCount);
        Assert.Equal(2, model.Page);
    }

    // ---------- resolving ----------

    [Theory]
    [InlineData(ReportStatus.Reviewed, "разгледан")]
    [InlineData(ReportStatus.Rejected, "отхвърлен")]
    public async Task Resolve_ShowsAMessage_AndKeepsTheFilterOfTheList(ReportStatus resolution, string messagePart)
    {
        _service.Setup(s => s.ResolveAsync(5, resolution)).ReturnsAsync(ServiceResult<Report>.Success(new Report()));
        var controller = Controller();

        var result = await controller.Resolve(5, resolution, "New", ReportTarget.Comment, page: 3);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("New", redirect.RouteValues!["status"]);
        Assert.Equal(ReportTarget.Comment, redirect.RouteValues["target"]);
        Assert.Equal(3, redirect.RouteValues["page"]);
        Assert.Contains(messagePart, controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Resolve_Refused_ShowsTheErrorInsteadOfSuccess()
    {
        _service.Setup(s => s.ResolveAsync(5, ReportStatus.New))
            .ReturnsAsync(ServiceResult<Report>.Failure(ServiceError.Invalid, "Невалиден статус."));
        var controller = Controller();

        await controller.Resolve(5, ReportStatus.New, null, null);

        Assert.Equal("Невалиден статус.", controller.TempDataText("ErrorMessage"));
        Assert.Null(controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task Resolve_UnknownReport_ReturnsNotFound()
    {
        _service.Setup(s => s.ResolveAsync(99, ReportStatus.Reviewed))
            .ReturnsAsync(ServiceResult<Report>.Failure(ServiceError.NotFound, "Сигналът не е намерен."));

        Assert.IsType<NotFoundResult>(await Controller().Resolve(99, ReportStatus.Reviewed, null, null));
    }

    // ---------- deleting the reported content ----------

    [Theory]
    [InlineData(ReportTarget.Comment, "Коментарът е изтрит")]
    [InlineData(ReportTarget.Resource, "Ресурсът е изтрит")]
    public async Task DeleteTarget_SaysWhatWasDeleted_AsTheSignedInAdministrator(ReportTarget target, string messagePart)
    {
        _service.Setup(s => s.DeleteTargetAsync(5, "admin"))
            .ReturnsAsync(ServiceResult<Report>.Success(new Report { Target = target }));
        var controller = Controller();

        var result = await controller.DeleteTarget(5, "all", null);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains(messagePart, controller.TempDataText("SuccessMessage"));
        _service.Verify(s => s.DeleteTargetAsync(5, "admin"), Times.Once);
    }

    [Fact]
    public async Task DeleteTarget_ContentAlreadyGone_ShowsTheError()
    {
        _service.Setup(s => s.DeleteTargetAsync(5, "admin"))
            .ReturnsAsync(ServiceResult<Report>.Failure(ServiceError.Conflict, "Ресурсът вече е изтрит."));
        var controller = Controller();

        await controller.DeleteTarget(5, null, null);

        Assert.Equal("Ресурсът вече е изтрит.", controller.TempDataText("ErrorMessage"));
        Assert.Null(controller.TempDataText("SuccessMessage"));
    }

    [Fact]
    public async Task DeleteTarget_UnknownReport_ReturnsNotFound()
    {
        _service.Setup(s => s.DeleteTargetAsync(99, "admin"))
            .ReturnsAsync(ServiceResult<Report>.Failure(ServiceError.NotFound, "Сигналът не е намерен."));

        Assert.IsType<NotFoundResult>(await Controller().DeleteTarget(99, null, null));
    }
}
