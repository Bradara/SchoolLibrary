using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Areas.Admin.Models;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Reports;
using System.Security.Claims;

namespace SchoolLibrary.Areas.Admin.Controllers
{
    // Moderation of the reports about resources and comments
    public class ReportsController : AdminController
    {
        private readonly IReportService _reports;

        public ReportsController(IReportService reports)
        {
            _reports = reports;
        }

        // GET: Admin/Reports?status=New&target=Comment&page=2
        // Without a status the list opens on the new reports; status=all shows every report
        public async Task<IActionResult> Index(string? status, ReportTarget? target, int page = 1)
        {
            var statusFilter = ParseStatus(status);

            var result = await _reports.SearchAsync(new ReportFilter
            {
                Status = statusFilter,
                Target = target,
                Page = page
            });

            return View(new ReportIndexViewModel
            {
                Status = statusFilter,
                Target = target,
                Reports = result.Items,
                Page = result.Page,
                TotalPages = result.TotalPages,
                TotalCount = result.TotalCount
            });
        }

        // POST: Admin/Reports/Resolve
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Resolve(int id, ReportStatus resolution, string? status, ReportTarget? target, int page = 1)
        {
            var result = await _reports.ResolveAsync(id, resolution);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = result.Message;
            }
            else
            {
                TempData["SuccessMessage"] = resolution == ReportStatus.Reviewed
                    ? "Сигналът е отбелязан като разгледан."
                    : "Сигналът е отхвърлен.";
            }

            return BackToList(status, target, page);
        }

        // POST: Admin/Reports/DeleteTarget
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTarget(int id, string? status, ReportTarget? target, int page = 1)
        {
            var result = await _reports.DeleteTargetAsync(id, User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = result.Message;
            }
            else
            {
                TempData["SuccessMessage"] = result.Value!.Target == ReportTarget.Comment
                    ? "Коментарът е изтрит, а сигналите за него са отбелязани като разгледани."
                    : "Ресурсът е изтрит, а сигналите за него са отбелязани като разгледани.";
            }

            return BackToList(status, target, page);
        }

        // No parameter means the new reports; "all" (or anything unknown) means no filter
        private static ReportStatus? ParseStatus(string? status)
        {
            if (string.IsNullOrEmpty(status))
            {
                return ReportStatus.New;
            }

            return Enum.TryParse<ReportStatus>(status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : null;
        }

        private IActionResult BackToList(string? status, ReportTarget? target, int page) =>
            RedirectToAction(nameof(Index), new { status, target, page });
    }
}
