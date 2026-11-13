using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Services.Reports;

/// <summary>What a user enters when reporting content. Exactly one of the two ids is set.</summary>
public class ReportInput
{
    public int? ResourceId { get; set; }

    public int? CommentId { get; set; }

    public ReportReason Reason { get; set; }

    public string? Details { get; set; }
}

/// <summary>Filter and paging for the list of reports. Every filter is optional.</summary>
public class ReportFilter
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    public ReportStatus? Status { get; set; }

    public ReportTarget? Target { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = DefaultPageSize;
}
