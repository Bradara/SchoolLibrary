using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Controllers
{
    // Teachers manage and review their own quizzes; an administrator sees everyone's.
    [Authorize(Roles = Roles.TeacherOrAdministrator)]
    public class QuizManagementController : Controller
    {
        private readonly IQuizService _quizzes;

        public QuizManagementController(IQuizService quizzes)
        {
            _quizzes = quizzes;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        private bool IsAdministrator => User.IsInRole(Roles.Administrator);

        // GET: QuizManagement — the quizzes the teacher owns (an administrator sees all)
        public async Task<IActionResult> Index(int page = 1)
        {
            var ownerId = IsAdministrator ? null : CurrentUserId;
            var result = await _quizzes.GetManagedAsync(ownerId, page);
            return View(result);
        }

        // GET: QuizManagement/Attempts/5 — who solved the quiz and with what score
        public async Task<IActionResult> Attempts(int id, int page = 1)
        {
            var result = await _quizzes.GetReviewAsync(id, CurrentUserId, IsAdministrator, page);

            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            return View(result.Value);
        }
    }
}
