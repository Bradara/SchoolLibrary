using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Exams;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Controllers
{
    // The exams panel: a teacher's classes, the practice quizzes given to them as exams, and the results.
    // An administrator sees every teacher's classes.
    [Authorize(Roles = Roles.TeacherOrAdministrator)]
    public class ExamsController : Controller
    {
        private readonly ISchoolClassService _classes;
        private readonly IExamAssignmentService _exams;
        private readonly IGradeScaleService _scales;

        public ExamsController(ISchoolClassService classes, IExamAssignmentService exams, IGradeScaleService scales)
        {
            _classes = classes;
            _exams = exams;
            _scales = scales;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        private bool IsAdministrator => User.IsInRole(Roles.Administrator);
        private string? OwnerScope => IsAdministrator ? null : CurrentUserId;

        // GET: Exams — the classes and their exams (optionally one class)
        public async Task<IActionResult> Index(int? classId)
        {
            return View(new ExamsIndexViewModel
            {
                Classes = await _classes.GetForOwnerAsync(OwnerScope),
                Exams = await _exams.GetForOwnerAsync(OwnerScope, classId),
                SelectedClassId = classId
            });
        }

        // ---------- Classes ----------

        // GET: Exams/CreateClass
        [HttpGet]
        public IActionResult CreateClass() => View("ClassForm", new SchoolClassFormViewModel());

        // POST: Exams/CreateClass
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateClass(SchoolClassFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View("ClassForm", vm);
            }

            var result = await _classes.CreateAsync(vm.Name, CurrentUserId);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(nameof(vm.Name), result.Message!);
                return View("ClassForm", vm);
            }

            TempData["SuccessMessage"] = $"Класът „{result.Value!.Name}“ е създаден.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Exams/EditClass/5
        [HttpGet]
        public async Task<IActionResult> EditClass(int id)
        {
            var schoolClass = await _classes.GetByIdAsync(id);
            if (schoolClass == null)
            {
                return NotFound();
            }

            if (!ExamPermissions.CanManage(schoolClass, CurrentUserId, IsAdministrator))
            {
                return Forbid();
            }

            ViewData["ClassId"] = id;
            return View("ClassForm", new SchoolClassFormViewModel { Name = schoolClass.Name });
        }

        // POST: Exams/EditClass/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditClass(int id, SchoolClassFormViewModel vm)
        {
            ViewData["ClassId"] = id;
            if (!ModelState.IsValid)
            {
                return View("ClassForm", vm);
            }

            var result = await _classes.RenameAsync(id, vm.Name, CurrentUserId, IsAdministrator);
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
                ModelState.AddModelError(nameof(vm.Name), result.Message!);
                return View("ClassForm", vm);
            }

            TempData["SuccessMessage"] = $"Класът е преименуван на „{result.Value!.Name}“.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Exams/DeleteClass/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteClass(int id)
        {
            var result = await _classes.DeleteAsync(id, CurrentUserId, IsAdministrator);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            TempData["SuccessMessage"] = $"Класът „{result.Value!.Name}“ е изтрит.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- Exams ----------

        // GET: Exams/Create?classId=1&quizId=2 — give a practice quiz to a class
        [HttpGet]
        public async Task<IActionResult> Create(int? classId, int? quizId)
        {
            var vm = new ExamAssignFormViewModel { SchoolClassId = classId ?? 0, QuizId = quizId ?? 0 };
            return View(await FillChoicesAsync(vm));
        }

        // POST: Exams/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ExamAssignFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(await FillChoicesAsync(vm));
            }

            var result = await _exams.CreateAsync(
                new ExamAssignmentInput(vm.QuizId, vm.SchoolClassId, vm.TimeLimitMinutes, vm.MaxLeaves, vm.CodeHints, vm.OneQuestionPerPage,
                    vm.GradingMethod, vm.GradeScaleId),
                CurrentUserId, IsAdministrator);

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message!);
                return View(await FillChoicesAsync(vm));
            }

            TempData["SuccessMessage"] = $"Изпитът е назначен. Код за учениците: {result.Value!.Code}";
            return RedirectToAction(nameof(Details), new { id = result.Value.Id });
        }

        // GET: Exams/Details/5 — the exam's code, state, settings and results
        public async Task<IActionResult> Details(int id, int page = 1)
        {
            var result = await _exams.GetResultsAsync(id, CurrentUserId, IsAdministrator, page);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            var exam = result.Value!.Exam;
            return View(new ExamDetailsViewModel
            {
                Results = result.Value,
                Settings = new ExamSettingsViewModel
                {
                    TimeLimitMinutes = exam.TimeLimitMinutes,
                    MaxLeaves = exam.MaxLeaves,
                    CodeHints = exam.CodeHints,
                    OneQuestionPerPage = exam.OneQuestionPerPage,
                    GradingMethod = exam.GradingMethod,
                    GradeScaleId = exam.GradeScaleId,
                    // The scales of the class's teacher (also when an administrator opens the exam)
                    Scales = exam.SchoolClass.OwnerId == null
                        ? new List<GradeScaleListItem>()
                        : await _scales.GetForOwnerAsync(exam.SchoolClass.OwnerId)
                }
            });
        }

        // POST: Exams/UpdateSettings/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSettings(int id, ExamSettingsViewModel settings)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
                return RedirectToAction(nameof(Details), new { id });
            }

            var result = await _exams.UpdateSettingsAsync(id, new ExamSettingsInput(settings.TimeLimitMinutes, settings.MaxLeaves,
                    settings.CodeHints, settings.OneQuestionPerPage, settings.GradingMethod, settings.GradeScaleId),
                CurrentUserId, IsAdministrator);
            return AfterChange(id, result, "Настройките са запазени.");
        }

        // POST: Exams/SetOpen/5 — open or close the exam for new attempts
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetOpen(int id, bool open)
        {
            var result = await _exams.SetOpenAsync(id, open, CurrentUserId, IsAdministrator);
            return AfterChange(id, result, open ? "Изпитът е отворен." : "Изпитът е затворен за нови опити.");
        }

        // POST: Exams/SetResults/5 — show or hide the results to the students
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetResults(int id, bool release)
        {
            var result = await _exams.SetResultsReleasedAsync(id, release, CurrentUserId, IsAdministrator);
            return AfterChange(id, result, release
                ? "Резултатите са активирани — учениците вече ги виждат."
                : "Резултатите са скрити от учениците.");
        }

        // POST: Exams/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _exams.DeleteAsync(id, CurrentUserId, IsAdministrator);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            TempData["SuccessMessage"] = "Изпитът и решенията му са изтрити.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Exams/Attempt/5 — one student's answers
        public async Task<IActionResult> Attempt(int id)
        {
            var result = await _exams.GetAttemptReviewAsync(id, CurrentUserId, IsAdministrator);
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
                TempData["ErrorMessage"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            return View(result.Value);
        }

        // POST: Exams/SetPoints/5 — the teacher's own points and note on one answer; empty points bring back
        // the automatic ones ("Върни автоматичните" sends the form without them)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPoints(int id, int questionId, string? points, string? comment, bool automatic,
            [FromServices] IAttemptGradingService grading)
        {
            decimal? given = null;
            if (!automatic && !string.IsNullOrWhiteSpace(points))
            {
                given = NumericAnswer.Parse(points);
                if (given == null)
                {
                    TempData["ErrorMessage"] = "Точките трябва да са число, например 1,5.";
                    return RedirectToAttemptQuestion(id, questionId);
                }
            }

            var result = await grading.SetPointsAsync(id, questionId, given, comment, CurrentUserId, IsAdministrator);
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
                TempData["SuccessMessage"] = given == null
                    ? "Точките на въпроса са автоматичните."
                    : $"Точките са променени. Резултат: {QuizPoints.Format(result.Value!.Score)} от {QuizPoints.Format(result.Value.MaxScore)}.";
            }
            else
            {
                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAttemptQuestion(id, questionId);
        }

        // Back to the attempt, scrolled to the question that was graded
        private RedirectToActionResult RedirectToAttemptQuestion(int attemptId, int questionId)
            => RedirectToAction(nameof(Attempt), null, new { id = attemptId }, "q-" + questionId);

        // POST: Exams/DeleteAttempt/5 — remove one student's attempt so they can take the exam again
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAttempt(int id)
        {
            var result = await _exams.DeleteAttemptAsync(id, CurrentUserId, IsAdministrator);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            if (result.Error == ServiceError.Forbidden)
            {
                return Forbid();
            }

            var attempt = result.Value!;
            TempData["SuccessMessage"] =
                $"Опитът на {attempt.StudentName} (№ {attempt.StudentNumber}) е изтрит. Ученикът може да реши изпита отново със същия номер.";
            return RedirectToAction(nameof(Details), new { id = attempt.ExamAssignmentId });
        }

        private async Task<ExamAssignFormViewModel> FillChoicesAsync(ExamAssignFormViewModel vm)
        {
            vm.Classes = await _classes.GetForOwnerAsync(OwnerScope);
            vm.Quizzes = await _exams.GetAssignableQuizzesAsync(OwnerScope);
            vm.Scales = await _scales.GetForOwnerAsync(OwnerScope);
            return vm;
        }

        private IActionResult AfterChange(int id, ServiceResult result, string successMessage)
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
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
