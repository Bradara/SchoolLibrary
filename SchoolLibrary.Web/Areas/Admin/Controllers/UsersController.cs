using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Areas.Admin.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Users;
using System.Security.Claims;

namespace SchoolLibrary.Areas.Admin.Controllers
{
    public class UsersController : AdminController
    {
        private readonly IUserService _users;

        public UsersController(IUserService users)
        {
            _users = users;
        }

        // GET: Admin/Users
        public async Task<IActionResult> Index(string? search, bool pending = false, int page = 1)
        {
            var result = await _users.SearchAsync(new UserFilter
            {
                SearchText = search,
                OnlyPendingTeachers = pending,
                Page = page
            });

            return View(new UserIndexViewModel
            {
                Search = search,
                Pending = pending,
                Users = result.Items,
                Page = result.Page,
                TotalPages = result.TotalPages,
                TotalCount = result.TotalCount,
                CurrentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!
            });
        }

        // POST: Admin/Users/ApproveTeacher
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveTeacher(string id)
        {
            var result = await _users.ApproveTeacherAsync(id);
            return Report(result, $"{result.Value?.FullName} вече е учител.");
        }

        // POST: Admin/Users/RejectTeacher
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectTeacher(string id)
        {
            var result = await _users.RejectTeacherAsync(id);
            return Report(result, $"Заявката на {result.Value?.FullName} е отхвърлена.");
        }

        // POST: Admin/Users/SetAdministrator
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetAdministrator(string id, bool makeAdministrator)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var result = await _users.SetAdministratorAsync(id, makeAdministrator, currentUserId);
            var message = makeAdministrator
                ? $"{result.Value?.FullName} е администратор."
                : $"{result.Value?.FullName} вече не е администратор.";
            return Report(result, message);
        }

        // Shows the outcome and returns to the list; an unknown user is a 404
        private IActionResult Report(ServiceResult<UserListItem> result, string successMessage)
        {
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = successMessage;
            }
            else
            {
                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
