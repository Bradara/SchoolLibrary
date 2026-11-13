using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Services.Reports;

public class ReportService : IReportService
{
    public const int DetailsMaxLength = 500;

    private readonly ApplicationDbContext _context;
    private readonly IResourceService _resources;
    private readonly ICommentService _comments;

    public ReportService(ApplicationDbContext context, IResourceService resources, ICommentService comments)
    {
        _context = context;
        _resources = resources;
        _comments = comments;
    }

    public async Task<ServiceResult<Report>> CreateAsync(ReportInput input, string reporterId)
    {
        if (input.ResourceId.HasValue == input.CommentId.HasValue)
        {
            return Fail(ServiceError.Invalid, "Не е посочено какво докладвате.");
        }

        if (!Enum.IsDefined(input.Reason))
        {
            return Fail(ServiceError.Invalid, "Изберете причина.");
        }

        var details = string.IsNullOrWhiteSpace(input.Details) ? null : input.Details.Replace("\r\n", "\n").Trim();

        if (details != null && details.Length > DetailsMaxLength)
        {
            return Fail(ServiceError.Invalid, $"Описанието не може да бъде по-дълго от {DetailsMaxLength} символа.");
        }

        if (input.Reason == ReportReason.Other && details == null)
        {
            return Fail(ServiceError.Invalid, "Опишете причината.");
        }

        var report = new Report
        {
            Reason = input.Reason,
            Details = details,
            Status = ReportStatus.New,
            CreatedOn = DateTime.UtcNow,
            ReporterId = reporterId
        };

        if (input.CommentId.HasValue)
        {
            var comment = await _context.Comments
                .Include(c => c.Resource)
                .FirstOrDefaultAsync(c => c.Id == input.CommentId.Value);

            if (comment == null)
            {
                return Fail(ServiceError.NotFound, "Коментарът не е намерен.");
            }

            report.Target = ReportTarget.Comment;
            report.CommentId = comment.Id;
            report.ResourceId = comment.ResourceId;
            report.TargetTitle = comment.Resource.Title;
            report.TargetText = comment.Text;
        }
        else
        {
            var resource = await _context.Resources.FirstOrDefaultAsync(r => r.Id == input.ResourceId!.Value);

            if (resource == null)
            {
                return Fail(ServiceError.NotFound, "Ресурсът не е намерен.");
            }

            report.Target = ReportTarget.Resource;
            report.ResourceId = resource.Id;
            report.TargetTitle = resource.Title;
        }

        // One open report per user and content: a second click is not a second report
        var alreadyReported = await _context.Reports.AnyAsync(r =>
            r.ReporterId == reporterId
            && r.Status == ReportStatus.New
            && r.Target == report.Target
            && (report.Target == ReportTarget.Comment ? r.CommentId == report.CommentId : r.ResourceId == report.ResourceId));

        if (alreadyReported)
        {
            return Fail(ServiceError.Conflict, "Вече сте подали сигнал за това съдържание, който още не е разгледан.");
        }

        _context.Reports.Add(report);
        await _context.SaveChangesAsync();

        return ServiceResult<Report>.Success(report);
    }

    public async Task<PagedResult<Report>> SearchAsync(ReportFilter filter)
    {
        var query = _context.Reports.AsNoTracking().AsQueryable();

        if (filter.Status.HasValue)
        {
            var status = filter.Status.Value;
            query = query.Where(r => r.Status == status);
        }

        if (filter.Target.HasValue)
        {
            var target = filter.Target.Value;
            query = query.Where(r => r.Target == target);
        }

        var totalCount = await query.CountAsync();

        var pageSize = Math.Clamp(filter.PageSize, 1, ReportFilter.MaxPageSize);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var page = Math.Clamp(filter.Page, 1, Math.Max(totalPages, 1));

        var items = await query
            .OrderByDescending(r => r.CreatedOn)
            .ThenByDescending(r => r.Id)
            .Include(r => r.Reporter)
            .Include(r => r.Comment!).ThenInclude(c => c.Author)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Report>(items, totalCount, page, pageSize);
    }

    public async Task<ServiceResult<Report>> ResolveAsync(int reportId, ReportStatus status)
    {
        if (status != ReportStatus.Reviewed && status != ReportStatus.Rejected)
        {
            return Fail(ServiceError.Invalid, "Сигналът може да бъде само разгледан или отхвърлен.");
        }

        var report = await _context.Reports.FirstOrDefaultAsync(r => r.Id == reportId);
        if (report == null)
        {
            return Fail(ServiceError.NotFound, "Сигналът не е намерен.");
        }

        report.Status = status;
        report.ReviewedOn = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return ServiceResult<Report>.Success(report);
    }

    public async Task<ServiceResult<Report>> DeleteTargetAsync(int reportId, string administratorId)
    {
        var report = await _context.Reports.FirstOrDefaultAsync(r => r.Id == reportId);
        if (report == null)
        {
            return Fail(ServiceError.NotFound, "Сигналът не е намерен.");
        }

        // The open reports that this deletion settles: this one and the others about the same content.
        // Deleting a resource also removes its comments, so reports about those comments are settled too.
        var settled = report.Target == ReportTarget.Comment
            ? await OpenReportIdsAsync(r => r.CommentId == report.CommentId)
            : await OpenReportIdsAsync(r => r.ResourceId == report.ResourceId);

        ServiceResult deleted;
        if (report.Target == ReportTarget.Comment)
        {
            if (report.CommentId == null)
            {
                return Fail(ServiceError.Conflict, "Коментарът вече е изтрит.");
            }

            deleted = await _comments.DeleteAsync(report.CommentId.Value, administratorId, isAdministrator: true);
        }
        else
        {
            if (report.ResourceId == null)
            {
                return Fail(ServiceError.Conflict, "Ресурсът вече е изтрит.");
            }

            deleted = await _resources.DeleteAsync(report.ResourceId.Value, administratorId, isAdministrator: true);
        }

        if (!deleted.Succeeded)
        {
            return Fail(deleted.Error, deleted.Message!);
        }

        settled.Add(report.Id);
        var reports = await _context.Reports.Where(r => settled.Contains(r.Id)).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var settledReport in reports)
        {
            settledReport.Status = ReportStatus.Reviewed;
            settledReport.ReviewedOn = now;
        }

        await _context.SaveChangesAsync();

        return ServiceResult<Report>.Success(report);
    }

    private async Task<HashSet<int>> OpenReportIdsAsync(System.Linq.Expressions.Expression<Func<Report, bool>> sameContent)
    {
        var ids = await _context.Reports
            .Where(r => r.Status == ReportStatus.New)
            .Where(sameContent)
            .Select(r => r.Id)
            .ToListAsync();

        return ids.ToHashSet();
    }

    private static ServiceResult<Report> Fail(ServiceError error, string message) =>
        ServiceResult<Report>.Failure(error, message);
}
