using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Areas.Admin.Models;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Areas.Admin.Controllers
{
    public class CategoriesController : AdminController
    {
        private readonly ICategoryService _categories;

        public CategoriesController(ICategoryService categories)
        {
            _categories = categories;
        }

        // GET: Categories
        public async Task<IActionResult> Index()
        {
            return View(await _categories.GetAllAsync());
        }

        // GET: Categories/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var category = await _categories.GetWithResourcesAsync(id.Value);
            if (category == null) return NotFound();

            return View(category);
        }

        // GET: Categories/Create
        public IActionResult Create()
        {
            return View(new CategoryFormViewModel());
        }

        // POST: Categories/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CategoryFormViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var result = await _categories.CreateAsync(model.ToInput());
            if (!result.Succeeded)
            {
                ModelState.AddModelError(nameof(CategoryFormViewModel.Name), result.Message!);
                return View(model);
            }

            TempData["SuccessMessage"] = $"Създаден е предмет {result.Value!.Name}.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Categories/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var category = await _categories.GetByIdAsync(id.Value);
            if (category == null) return NotFound();

            return View(CategoryFormViewModel.From(category));
        }

        // POST: Categories/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CategoryFormViewModel model)
        {
            if (id != model.Id) return NotFound();
            if (!ModelState.IsValid) return View(model);

            var result = await _categories.UpdateAsync(id, model.ToInput());
            if (result.Error == ServiceError.NotFound) return NotFound();
            if (!result.Succeeded)
            {
                ModelState.AddModelError(nameof(CategoryFormViewModel.Name), result.Message!);
                return View(model);
            }

            TempData["SuccessMessage"] = $"Предмет {result.Value!.Name} е променен.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Categories/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var category = await _categories.GetByIdAsync(id.Value);
            if (category == null) return NotFound();

            return View(category);
        }

        // POST: Categories/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var result = await _categories.DeleteAsync(id);

            if (result.Error == ServiceError.NotFound) return NotFound();
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            TempData["SuccessMessage"] = $"Предмет {result.Value!.Name} е изтрит.";
            return RedirectToAction(nameof(Index));
        }
    }
}
