using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

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

        // GET: QuizManagement/Details/5 — the hub for managing one quiz
        public async Task<IActionResult> Details(int id)
        {
            var quiz = await _quizzes.GetByIdAsync(id);
            if (quiz == null)
            {
                return NotFound();
            }

            if (!QuizPermissions.CanManage(quiz, CurrentUserId, IsAdministrator))
            {
                return Forbid();
            }

            return View(new QuizManageViewModel { Quiz = quiz });
        }

        // GET: QuizManagement/Create
        public IActionResult Create() => View(new QuizFormViewModel());

        // POST: QuizManagement/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(QuizFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var result = await _quizzes.CreateAsync(ToInput(vm), CurrentUserId);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View(vm);
            }

            TempData["SuccessMessage"] = $"Тестът „{result.Value!.Title}“ е създаден. Добавете въпроси и публикувайте.";
            return RedirectToAction(nameof(Details), new { id = result.Value.Id });
        }

        // GET: QuizManagement/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var quiz = await _quizzes.GetByIdAsync(id);
            if (quiz == null)
            {
                return NotFound();
            }

            if (!QuizPermissions.CanManage(quiz, CurrentUserId, IsAdministrator))
            {
                return Forbid();
            }

            return View(new QuizFormViewModel
            {
                Title = quiz.Title,
                Description = quiz.Description,
                Mode = quiz.Mode,
                TimeLimitMinutes = quiz.TimeLimitMinutes
            });
        }

        // POST: QuizManagement/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, QuizFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var result = await _quizzes.UpdateAsync(id, ToInput(vm), CurrentUserId, IsAdministrator);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View(vm);
            }

            TempData["SuccessMessage"] = "Промените са записани.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: QuizManagement/AddQuestion/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddQuestion(int id, QuestionFormViewModel vm)
        {
            // Drop empty option rows and keep track of which kept option is the correct one
            var options = new List<string>();
            var correctIndex = -1;
            var raw = vm.Options ?? new List<string>();
            for (var i = 0; i < raw.Count; i++)
            {
                var text = (raw[i] ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                if (i == vm.CorrectIndex)
                {
                    correctIndex = options.Count;
                }

                options.Add(text);
            }

            var result = await _quizzes.AddQuestionAsync(id, new QuestionInput(vm.Text ?? string.Empty, options, correctIndex), CurrentUserId, IsAdministrator);
            return AfterQuestionChange(id, result, "Въпросът е добавен.");
        }

        // POST: QuizManagement/DeleteQuestion
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteQuestion(int id, int questionId)
        {
            var result = await _quizzes.DeleteQuestionAsync(questionId, CurrentUserId, IsAdministrator);
            return AfterQuestionChange(id, result, "Въпросът е изтрит.");
        }

        // POST: QuizManagement/SetPublished/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPublished(int id, bool publish)
        {
            var result = await _quizzes.SetPublishedAsync(id, publish, CurrentUserId, IsAdministrator);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] =
                result.Succeeded ? (publish ? "Тестът е публикуван." : "Тестът е скрит.") : result.Message;
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: QuizManagement/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _quizzes.DeleteAsync(id, CurrentUserId, IsAdministrator);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            TempData["SuccessMessage"] = $"Тестът „{result.Value!.Title}“ е изтрит.";
            return RedirectToAction(nameof(Index));
        }

        private static QuizInput ToInput(QuizFormViewModel vm) =>
            new(vm.Title, vm.Description, vm.Mode, vm.TimeLimitMinutes, null);

        // Shared handling for adding/deleting a question: map the outcome to a response
        private IActionResult AfterQuestionChange(int quizId, ServiceResult result, string successMessage)
        {
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            TempData[result.Succeeded ? "SuccessMessage" : "ErrorMessage"] = result.Succeeded ? successMessage : result.Message;
            return RedirectToAction(nameof(Details), new { id = quizId });
        }
    }
}
