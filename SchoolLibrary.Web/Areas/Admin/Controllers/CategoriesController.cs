using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            return View();
        }

        // POST: Categories/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name")] Category category)
        {
            if (!ModelState.IsValid) return View(category);

            var result = await _categories.CreateAsync(category.Name);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(nameof(Category.Name), result.Message!);
                return View(category);
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

            return View(category);
        }

        // POST: Categories/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name")] Category category)
        {
            if (id != category.Id) return NotFound();
            if (!ModelState.IsValid) return View(category);

            var result = await _categories.UpdateAsync(id, category.Name);
            if (result.Error == ServiceError.NotFound) return NotFound();
            if (!result.Succeeded)
            {
                ModelState.AddModelError(nameof(Category.Name), result.Message!);
                return View(category);
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
