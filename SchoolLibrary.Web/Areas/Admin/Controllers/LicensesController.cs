using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Areas.Admin.Models;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Licenses;

namespace SchoolLibrary.Areas.Admin.Controllers
{
    public class LicensesController : AdminController
    {
        private readonly ILicenseService _licenses;

        public LicensesController(ILicenseService licenses)
        {
            _licenses = licenses;
        }

        // GET: Admin/Licenses
        public async Task<IActionResult> Index()
        {
            return View(await _licenses.GetAllAsync());
        }

        // GET: Admin/Licenses/Create
        public IActionResult Create()
        {
            return View(new LicenseFormViewModel());
        }

        // POST: Admin/Licenses/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(LicenseFormViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var result = await _licenses.CreateAsync(model.ToInput());
            if (!result.Succeeded)
            {
                AddError(result);
                return View(model);
            }

            TempData["SuccessMessage"] = $"Създаден е лиценз {result.Value!.Name}.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Admin/Licenses/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var license = await _licenses.GetByIdAsync(id.Value);
            if (license == null) return NotFound();

            return View(ToForm(license));
        }

        // POST: Admin/Licenses/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, LicenseFormViewModel model)
        {
            if (id != model.Id) return NotFound();
            if (!ModelState.IsValid) return View(model);

            var result = await _licenses.UpdateAsync(id, model.ToInput());
            if (result.Error == ServiceError.NotFound) return NotFound();
            if (!result.Succeeded)
            {
                AddError(result);
                return View(model);
            }

            TempData["SuccessMessage"] = $"Лиценз {result.Value!.Name} е променен.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Admin/Licenses/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var license = await _licenses.GetByIdAsync(id.Value);
            if (license == null) return NotFound();

            return View(license);
        }

        // POST: Admin/Licenses/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var result = await _licenses.DeleteAsync(id);

            if (result.Error == ServiceError.NotFound) return NotFound();
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            TempData["SuccessMessage"] = $"Лиценз {result.Value!.Name} е изтрит.";
            return RedirectToAction(nameof(Index));
        }

        private static LicenseFormViewModel ToForm(License license) => new()
        {
            Id = license.Id,
            Name = license.Name,
            Description = license.Description,
            Url = license.Url
        };

        // A name that is taken goes on the name field; anything else on the form
        private void AddError(ServiceResult result)
        {
            var field = result.Error == ServiceError.Conflict ? nameof(LicenseFormViewModel.Name) : string.Empty;
            ModelState.AddModelError(field, result.Message!);
        }
    }
}
