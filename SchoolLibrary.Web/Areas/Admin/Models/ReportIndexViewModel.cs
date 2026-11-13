using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Areas.Admin.Models;

public class ReportIndexViewModel
{
    // Filters. A null status means "all"; the list opens on the new reports.
    public ReportStatus? Status { get; set; }
    public ReportTarget? Target { get; set; }

    public IReadOnlyList<Report> Reports { get; set; } = new List<Report>();

    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }

    /// <summary>The value of the "status" query parameter for the current filter ("all" for no filter).</summary>
    public string StatusParameter => Status?.ToString() ?? "all";
}
