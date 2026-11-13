using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Reports;

/// <summary>Reports about resources and comments, and their moderation by administrators.</summary>
public interface IReportService
{
    /// <summary>
    /// Creates a report. Fails when it is not clear what is reported, the reason is missing, the content
    /// does not exist, or the same user already has an unreviewed report about the same content.
    /// </summary>
    Task<ServiceResult<Report>> CreateAsync(ReportInput input, string reporterId);

    /// <summary>One page of reports, newest first.</summary>
    Task<PagedResult<Report>> SearchAsync(ReportFilter filter);

    /// <summary>Marks the report as reviewed or rejected (any other status is refused).</summary>
    Task<ServiceResult<Report>> ResolveAsync(int reportId, Data.Enums.ReportStatus status);

    /// <summary>
    /// Deletes the reported resource or comment and marks the report, and every other open report about the
    /// same content, as reviewed. Fails when the content is already gone.
    /// </summary>
    Task<ServiceResult<Report>> DeleteTargetAsync(int reportId, string administratorId);
}
