using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
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
        private readonly IQuizItemService _items;
        private readonly ISectionService _sections;
        private readonly IQuestionBankService _bank;

        public QuizManagementController(IQuizService quizzes, IQuizItemService items, ISectionService sections, IQuestionBankService bank)
        {
            _quizzes = quizzes;
            _items = items;
            _sections = sections;
            _bank = bank;
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

        // GET: QuizManagement/Export/5 — the quiz's own and pinned questions as a Moodle XML file
        public async Task<IActionResult> Export(int id, [FromServices] IQuestionExportService export)
        {
            var result = await export.ExportQuizAsync(id, CurrentUserId, IsAdministrator);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            var file = result.Value!;
            return File(System.Text.Encoding.UTF8.GetBytes(file.Content), "application/xml", file.FileName);
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

            return View(await BuildManageAsync(quiz));
        }

        private async Task<QuizManageViewModel> BuildManageAsync(Quiz quiz, QuestionFormViewModel? newQuestion = null)
        {
            // An administrator editing someone else's quiz sees that owner's bank
            var ownerScope = IsAdministrator ? quiz.OwnerId : CurrentUserId;
            return new QuizManageViewModel
            {
                Quiz = quiz,
                NewQuestion = newQuestion ?? new QuestionFormViewModel(),
                Items = await _items.GetItemsAsync(quiz.Id),
                Sections = SectionPaths.Build(await _sections.GetAllForOwnerAsync(ownerScope)),
                BankQuestions = await _bank.GetForOwnerAsync(ownerScope)
            };
        }

        // POST: QuizManagement/AddRandomItem/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddRandomItem(int id, int sectionId, int pickCount)
        {
            var result = await _items.AddRandomSectionAsync(id, sectionId, pickCount, CurrentUserId, IsAdministrator);
            return AfterItemChange(id, result, "Добавен е произволен раздел към състава.");
        }

        // POST: QuizManagement/AddFixedItem/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddFixedItem(int id, int questionId)
        {
            var result = await _items.AddFixedQuestionAsync(id, questionId, CurrentUserId, IsAdministrator);
            return AfterItemChange(id, result, "Въпросът е добавен към състава.");
        }

        // POST: QuizManagement/MoveItem/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveItem(int id, int itemId, bool up)
        {
            var result = await _items.MoveItemAsync(itemId, up, CurrentUserId, IsAdministrator);
            return AfterItemChange(id, result, null);
        }

        // POST: QuizManagement/DeleteItem/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteItem(int id, int itemId)
        {
            var result = await _items.DeleteItemAsync(itemId, CurrentUserId, IsAdministrator);
            return AfterItemChange(id, result, "Елементът е премахнат от състава.");
        }

        // Shared handling for composition changes: map the outcome to a response
        private IActionResult AfterItemChange(int quizId, ServiceResult<SchoolLibrary.Data.Models.QuizItem> result, string? successMessage)
        {
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            if (result.Succeeded)
            {
                if (successMessage != null)
                {
                    TempData["SuccessMessage"] = successMessage;
                }
            }
            else
            {
                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAction(nameof(Details), new { id = quizId });
        }

        // GET: QuizManagement/Create
        public IActionResult Create() => View(new QuizFormViewModel());

        // POST: QuizManagement/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(QuizFormViewModel vm)
        {
            // New quizzes are practice quizzes; exams are class assignments (ExamsController)
            vm.Mode = QuizMode.Practice;
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
                TimeLimitMinutes = quiz.TimeLimitMinutes,
                MaxLeaves = quiz.MaxLeaves,
                ShuffleQuestions = quiz.ShuffleQuestions,
                ShuffleOptions = quiz.ShuffleOptions
            });
        }

        // POST: QuizManagement/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, QuizFormViewModel vm)
        {
            // A practice quiz cannot be turned into an exam-mode quiz (only older exams keep that mode)
            var existing = await _quizzes.GetByIdAsync(id);
            if (existing?.Mode == QuizMode.Practice)
            {
                vm.Mode = QuizMode.Practice;
            }

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
            // The question is created in the bank and pinned to the quiz as a fixed item
            var result = await _items.AddNewQuestionAsync(id, vm.ToInput(), CurrentUserId, IsAdministrator);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "Въпросът е добавен в банката и към теста.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Invalid (or a conflict): re-render the manage page with the typed-in question kept
            var quiz = await _quizzes.GetByIdAsync(id);
            if (quiz == null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, result.Message!);
            return View(nameof(Details), await BuildManageAsync(quiz, vm));
        }

        // GET: QuizManagement/EditQuestion/5?questionId=9
        public async Task<IActionResult> EditQuestion(int id, int questionId)
        {
            var question = await _quizzes.GetQuestionAsync(questionId);
            if (question?.Quiz == null)
            {
                return NotFound();
            }

            if (!QuizPermissions.CanManage(question.Quiz, CurrentUserId, IsAdministrator))
            {
                return Forbid();
            }

            ViewBag.QuizId = question.QuizId;
            ViewBag.QuestionId = questionId;
            ViewBag.AnsweredAttempts = await _bank.CountAnsweredAttemptsAsync(questionId);
            return View(QuestionFormViewModel.FromQuestion(question));
        }

        // POST: QuizManagement/EditQuestion/5?questionId=9
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditQuestion(int id, int questionId, QuestionFormViewModel vm)
        {
            var result = await _quizzes.UpdateQuestionAsync(questionId, vm.ToInput(), CurrentUserId, IsAdministrator);
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
                ViewBag.QuizId = id;
                ViewBag.QuestionId = questionId;
                ViewBag.AnsweredAttempts = await _bank.CountAnsweredAttemptsAsync(questionId);
                return View(vm);
            }

            TempData["SuccessMessage"] = "Въпросът е променен.";
            return RedirectToAction(nameof(Details), new { id });
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
            new(vm.Title, vm.Description, vm.Mode, vm.TimeLimitMinutes, null, vm.MaxLeaves,
                vm.ShuffleQuestions, vm.ShuffleOptions);

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
