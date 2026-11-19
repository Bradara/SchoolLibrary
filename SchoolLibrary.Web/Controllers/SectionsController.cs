using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Controllers
{
    // Teachers manage their own question bank (sections and the questions in them); an administrator sees everyone's.
    [Authorize(Roles = Roles.TeacherOrAdministrator)]
    public class SectionsController : Controller
    {
        private readonly ISectionService _sections;
        private readonly IQuestionBankService _questions;

        public SectionsController(ISectionService sections, IQuestionBankService questions)
        {
            _sections = sections;
            _questions = questions;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        private bool IsAdministrator => User.IsInRole(Roles.Administrator);

        // GET: Sections — the sections the teacher owns (an administrator sees all)
        public async Task<IActionResult> Index(int page = 1)
        {
            var ownerId = IsAdministrator ? null : CurrentUserId;
            return View(await _sections.GetManagedAsync(ownerId, page));
        }

        // GET: Sections/Details/5 — a section with its questions
        public async Task<IActionResult> Details(int id)
        {
            var section = await _sections.GetByIdAsync(id);
            if (section == null)
            {
                return NotFound();
            }

            if (!SectionPermissions.CanManage(section, CurrentUserId, IsAdministrator))
            {
                return Forbid();
            }

            return View(new SectionManageViewModel
            {
                Section = section,
                Questions = await _questions.GetBySectionAsync(id)
            });
        }

        // GET: Sections/Create
        public IActionResult Create() => View(new SectionFormViewModel());

        // POST: Sections/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SectionFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var result = await _sections.CreateAsync(new SectionInput(vm.Name, vm.Description), CurrentUserId);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View(vm);
            }

            TempData["SuccessMessage"] = $"Разделът „{result.Value!.Name}“ е създаден.";
            return RedirectToAction(nameof(Details), new { id = result.Value.Id });
        }

        // GET: Sections/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var section = await _sections.GetByIdAsync(id);
            if (section == null)
            {
                return NotFound();
            }

            if (!SectionPermissions.CanManage(section, CurrentUserId, IsAdministrator))
            {
                return Forbid();
            }

            return View(new SectionFormViewModel { Name = section.Name, Description = section.Description });
        }

        // POST: Sections/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SectionFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var result = await _sections.UpdateAsync(id, new SectionInput(vm.Name, vm.Description), CurrentUserId, IsAdministrator);
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

        // POST: Sections/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _sections.DeleteAsync(id, CurrentUserId, IsAdministrator);
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
                TempData["SuccessMessage"] = $"Разделът „{result.Value!.Name}“ е изтрит.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Sections/AddQuestion/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddQuestion(int id, QuestionFormViewModel vm)
        {
            var result = await _questions.CreateAsync(id, vm.ToInput(), CurrentUserId, IsAdministrator);
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
                TempData["SuccessMessage"] = "Въпросът е добавен.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Invalid: re-render the section page with the typed-in question kept
            var section = await _sections.GetByIdAsync(id);
            if (section == null)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, result.Message!);
            return View(nameof(Details), new SectionManageViewModel
            {
                Section = section,
                Questions = await _questions.GetBySectionAsync(id),
                NewQuestion = vm
            });
        }

        // GET: Sections/EditQuestion/5?questionId=9
        public async Task<IActionResult> EditQuestion(int id, int questionId)
        {
            var question = await _questions.GetQuestionAsync(questionId);
            if (question?.Section == null)
            {
                return NotFound();
            }

            if (!SectionPermissions.CanManage(question.Section, CurrentUserId, IsAdministrator))
            {
                return Forbid();
            }

            var options = question.Options.OrderBy(o => o.Order).ToList();
            ViewBag.SectionId = question.SectionId;
            ViewBag.QuestionId = questionId;
            return View(new QuestionFormViewModel
            {
                Text = question.Text,
                Options = options.Select(o => o.Text).ToList(),
                CorrectIndex = options.FindIndex(o => o.IsCorrect),
                Points = question.Points
            });
        }

        // POST: Sections/EditQuestion/5?questionId=9
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditQuestion(int id, int questionId, QuestionFormViewModel vm)
        {
            var result = await _questions.UpdateAsync(questionId, vm.ToInput(), CurrentUserId, IsAdministrator);
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
                ViewBag.SectionId = id;
                ViewBag.QuestionId = questionId;
                return View(vm);
            }

            TempData["SuccessMessage"] = "Въпросът е променен.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Sections/DeleteQuestion
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteQuestion(int id, int questionId)
        {
            var result = await _questions.DeleteAsync(questionId, CurrentUserId, IsAdministrator);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            TempData["SuccessMessage"] = "Въпросът е изтрит.";
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
