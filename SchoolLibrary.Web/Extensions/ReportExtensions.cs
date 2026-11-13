using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Extensions;

/// <summary>Bulgarian names and badge colors for the report enums, used by the views.</summary>
public static class ReportExtensions
{
    public static string DisplayName(this ReportReason reason) => reason switch
    {
        ReportReason.Copyright => "Нарушено авторско право",
        ReportReason.Inappropriate => "Неподходящо съдържание",
        ReportReason.BrokenLink => "Невалиден линк",
        _ => "Друго"
    };

    public static string DisplayName(this ReportStatus status) => status switch
    {
        ReportStatus.New => "Нов",
        ReportStatus.Reviewed => "Разгледан",
        _ => "Отхвърлен"
    };

    public static string DisplayName(this ReportTarget target) => target switch
    {
        ReportTarget.Comment => "Коментар",
        _ => "Ресурс"
    };

    /// <summary>Bootstrap background class of the status badge.</summary>
    public static string BadgeClass(this ReportStatus status) => status switch
    {
        ReportStatus.New => "text-bg-warning",
        ReportStatus.Reviewed => "text-bg-success",
        _ => "text-bg-secondary"
    };
}
