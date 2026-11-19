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

            return View(await BuildManageAsync(section));
        }

        // GET: Sections/Create (optionally a subsection, with ?parentId=5)
        public async Task<IActionResult> Create(int? parentId)
        {
            var vm = new SectionFormViewModel { ParentId = parentId };
            if (parentId != null)
            {
                var parent = await _sections.GetByIdAsync(parentId.Value);
                if (parent == null)
                {
                    return NotFound();
                }

                if (!SectionPermissions.CanManage(parent, CurrentUserId, IsAdministrator))
                {
                    return Forbid();
                }

                vm.ParentName = parent.Name;
            }

            return View(vm);
        }

        // POST: Sections/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SectionFormViewModel vm)
        {
            // When creating a subsection, the current user must be able to manage the parent
            if (vm.ParentId != null)
            {
                var parent = await _sections.GetByIdAsync(vm.ParentId.Value);
                if (parent == null)
                {
                    return NotFound();
                }

                if (!SectionPermissions.CanManage(parent, CurrentUserId, IsAdministrator))
                {
                    return Forbid();
                }

                vm.ParentName = parent.Name;
            }

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var result = await _sections.CreateAsync(new SectionInput(vm.Name, vm.Description), CurrentUserId, vm.ParentId);
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

            ViewBag.ParentChoices = await _sections.GetParentCandidatesAsync(id, section.OwnerId);
            return View(new SectionFormViewModel
            {
                Name = section.Name,
                Description = section.Description,
                ParentId = section.ParentId
            });
        }

        // POST: Sections/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SectionFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                await PopulateParentChoicesAsync(id);
                return View(vm);
            }

            var result = await _sections.UpdateAsync(id, new SectionInput(vm.Name, vm.Description), CurrentUserId, IsAdministrator, vm.ParentId);
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
                await PopulateParentChoicesAsync(id);
                return View(vm);
            }

            TempData["SuccessMessage"] = "Промените са записани.";
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task PopulateParentChoicesAsync(int sectionId)
        {
            var section = await _sections.GetByIdAsync(sectionId);
            ViewBag.ParentChoices = section == null
                ? new List<Data.Models.Section>()
                : await _sections.GetParentCandidatesAsync(sectionId, section.OwnerId);
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
            return View(nameof(Details), await BuildManageAsync(section, vm));
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

        // POST: Sections/ImportAiken/5 — bulk-load questions from pasted Aiken text or an uploaded .txt
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(2 * 1024 * 1024)]
        public async Task<IActionResult> ImportAiken(int id, string? aikenText, IFormFile? aikenFile = null)
        {
            // An uploaded file wins over the textarea when present
            var text = aikenText ?? string.Empty;
            if (aikenFile is { Length: > 0 })
            {
                using var reader = new StreamReader(aikenFile.OpenReadStream());
                text = await reader.ReadToEndAsync();
            }

            var result = await _questions.ImportAikenAsync(id, text, CurrentUserId, IsAdministrator);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            var summary = result.Value!;
            TempData["SuccessMessage"] = $"Импортирани въпроси: {summary.Added}.";
            if (summary.Errors.Count > 0)
            {
                TempData["ErrorMessage"] = "Пропуснати: " + string.Join(" · ", summary.Errors.Take(10))
                    + (summary.Errors.Count > 10 ? " …" : string.Empty);
            }

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

        // Loads a section's subsections, ancestors (for the breadcrumb) and questions for the manage page
        private async Task<SectionManageViewModel> BuildManageAsync(Data.Models.Section section, QuestionFormViewModel? newQuestion = null)
            => new()
            {
                Section = section,
                Subsections = await _sections.GetChildrenAsync(section.Id),
                Ancestors = await _sections.GetAncestorsAsync(section.Id),
                Questions = await _questions.GetBySectionAsync(section.Id),
                NewQuestion = newQuestion ?? new QuestionFormViewModel()
            };
    }
}
