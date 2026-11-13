using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Reports;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.ViewModels;
using System.Security.Claims;

namespace SchoolLibrary.Controllers
{
    // Every signed-in user can report a resource or a comment; administrators handle the reports
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly IReportService _reports;
        private readonly IResourceService _resources;
        private readonly ICommentService _comments;

        public ReportsController(IReportService reports, IResourceService resources, ICommentService comments)
        {
            _reports = reports;
            _resources = resources;
            _comments = comments;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // GET: Reports/Create?resourceId=5  or  Reports/Create?commentId=7
        public async Task<IActionResult> Create(int? resourceId, int? commentId)
        {
            var vm = new ReportFormViewModel { ResourceId = resourceId, CommentId = commentId };

            if (!await LoadTargetAsync(vm))
            {
                return NotFound();
            }

            return View(vm);
        }

        // POST: Reports/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReportFormViewModel vm)
        {
            if (!await LoadTargetAsync(vm))
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var result = await _reports.CreateAsync(new ReportInput
                {
                    ResourceId = vm.ResourceId,
                    CommentId = vm.CommentId,
                    Reason = vm.Reason!.Value,
                    Details = vm.Details
                }, CurrentUserId);

                if (result.Succeeded)
                {
                    TempData["SuccessMessage"] = "Благодарим! Сигналът е изпратен и ще бъде прегледан от администратор.";
                    return RedirectToAction("Details", "Resources", new { id = result.Value!.ResourceId }, vm.IsComment ? "comments" : null);
                }

                if (result.Error == ServiceError.NotFound)
                {
                    return NotFound();
                }

                ModelState.AddModelError(string.Empty, result.Message!);
            }

            return View(vm);
        }

        // Fills in what is being reported; false when it does not exist or it is unclear what is reported
        private async Task<bool> LoadTargetAsync(ReportFormViewModel vm)
        {
            if (vm.ResourceId.HasValue == vm.CommentId.HasValue)
            {
                return false;
            }

            if (vm.CommentId.HasValue)
            {
                var comment = await _comments.GetAsync(vm.CommentId.Value);
                if (comment == null)
                {
                    return false;
                }

                vm.BackResourceId = comment.ResourceId;
                vm.TargetTitle = comment.Resource.Title;
                vm.TargetText = comment.Text;
                vm.TargetAuthor = comment.Author.FullName;
                return true;
            }

            var resource = await _resources.GetDetailsAsync(vm.ResourceId!.Value);
            if (resource == null)
            {
                return false;
            }

            vm.BackResourceId = resource.Id;
            vm.TargetTitle = resource.Title;
            return true;
        }
    }
}
