using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Exams;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Controllers
{
    // A teacher's own grade scales: the lowest percentage of the points for each grade. Exams point at a scale,
    // so a change applies to every exam that uses it. An administrator sees every teacher's scales.
    [Authorize(Roles = Roles.TeacherOrAdministrator)]
    public class GradeScalesController : Controller
    {
        private readonly IGradeScaleService _scales;

        public GradeScalesController(IGradeScaleService scales)
        {
            _scales = scales;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        private bool IsAdministrator => User.IsInRole(Roles.Administrator);

        // GET: GradeScales
        public async Task<IActionResult> Index()
            => View(await _scales.GetForOwnerAsync(IsAdministrator ? null : CurrentUserId));

        // GET: GradeScales/Create
        [HttpGet]
        public IActionResult Create() => View("Form", new GradeScaleFormViewModel());

        // POST: GradeScales/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(GradeScaleFormViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View("Form", vm);
            }

            var result = await _scales.CreateAsync(vm.ToInput(), CurrentUserId);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(result.Error == ServiceError.Conflict ? nameof(vm.Name) : string.Empty, result.Message!);
                return View("Form", vm);
            }

            TempData["SuccessMessage"] = $"Скалата „{result.Value!.Name}“ е създадена.";
            return RedirectToAction(nameof(Index));
        }

        // GET: GradeScales/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var result = await _scales.GetAsync(id, CurrentUserId, IsAdministrator);
            if (!result.Succeeded)
            {
                return Refused(result.Error);
            }

            ViewData["ScaleId"] = id;
            return View("Form", GradeScaleFormViewModel.From(result.Value!));
        }

        // POST: GradeScales/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, GradeScaleFormViewModel vm)
        {
            ViewData["ScaleId"] = id;
            if (!ModelState.IsValid)
            {
                return View("Form", vm);
            }

            var result = await _scales.UpdateAsync(id, vm.ToInput(), CurrentUserId, IsAdministrator);
            if (result.Error is ServiceError.NotFound or ServiceError.Forbidden)
            {
                return Refused(result.Error);
            }

            if (!result.Succeeded)
            {
                ModelState.AddModelError(result.Error == ServiceError.Conflict ? nameof(vm.Name) : string.Empty, result.Message!);
                return View("Form", vm);
            }

            TempData["SuccessMessage"] = $"Скалата „{result.Value!.Name}“ е запазена. Оценките на изпитите с нея са преизчислени.";
            return RedirectToAction(nameof(Index));
        }

        // POST: GradeScales/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _scales.DeleteAsync(id, CurrentUserId, IsAdministrator);
            if (result.Error is ServiceError.NotFound or ServiceError.Forbidden)
            {
                return Refused(result.Error);
            }

            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = $"Скалата „{result.Value!.Name}“ е изтрита.";
            }
            else
            {
                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        private IActionResult Refused(ServiceError error)
            => error == ServiceError.Forbidden ? Forbid() : NotFound();
    }
}
