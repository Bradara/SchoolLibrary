namespace SchoolLibrary.Data.Enums;

/// <summary>Why a user reported something. The numbers are stored in the database.</summary>
public enum ReportReason
{
    Copyright = 0,
    Inappropriate = 1,
    BrokenLink = 2,
    Other = 3
}

/// <summary>Where a report is in moderation. The numbers are stored in the database.</summary>
public enum ReportStatus
{
    New = 0,
    Reviewed = 1,
    Rejected = 2
}

/// <summary>What was reported. The numbers are stored in the database.</summary>
public enum ReportTarget
{
    Resource = 0,
    Comment = 1
}
