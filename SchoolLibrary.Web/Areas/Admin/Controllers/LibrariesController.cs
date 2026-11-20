using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolLibrary.Areas.Admin.Models;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.OnlineLibraries;

namespace SchoolLibrary.Areas.Admin.Controllers
{
    // Manages the catalog of online libraries shown on the public "Библиотеки" page
    public class LibrariesController : AdminController
    {
        private readonly IOnlineLibraryService _libraries;
        private readonly ILibraryIconService _icons;

        public LibrariesController(IOnlineLibraryService libraries, ILibraryIconService icons)
        {
            _libraries = libraries;
            _icons = icons;
        }

        // GET: Admin/Libraries
        public async Task<IActionResult> Index()
        {
            return View(await _libraries.GetAllAsync());
        }

        // GET: Admin/Libraries/Create
        public IActionResult Create()
        {
            return View(WithIcons(new OnlineLibraryFormViewModel()));
        }

        // POST: Admin/Libraries/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(OnlineLibraryFormViewModel model)
        {
            if (!ModelState.IsValid) return View(WithIcons(model));

            var result = await _libraries.CreateAsync(model.ToInput());
            if (!result.Succeeded)
            {
                AddError(result);
                return View(WithIcons(model));
            }

            TempData["SuccessMessage"] = $"Добавена е библиотека {result.Value!.Name}." + await DownloadIconAsync(result.Value.Id);
            return RedirectToAction(nameof(Index));
        }

        // GET: Admin/Libraries/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var library = await _libraries.GetByIdAsync(id.Value);
            if (library == null) return NotFound();

            return View(WithIcons(ToForm(library)));
        }

        // POST: Admin/Libraries/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, OnlineLibraryFormViewModel model)
        {
            if (id != model.Id) return NotFound();
            if (!ModelState.IsValid) return View(WithIcons(model));

            var result = await _libraries.UpdateAsync(id, model.ToInput());
            if (result.Error == ServiceError.NotFound) return NotFound();
            if (!result.Succeeded)
            {
                AddError(result);
                return View(WithIcons(model));
            }

            TempData["SuccessMessage"] = $"Библиотека {result.Value!.Name} е променена." + await DownloadIconAsync(result.Value.Id);
            return RedirectToAction(nameof(Index));
        }

        // GET: Admin/Libraries/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var library = await _libraries.GetByIdAsync(id.Value);
            if (library == null) return NotFound();

            return View(library);
        }

        // POST: Admin/Libraries/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var result = await _libraries.DeleteAsync(id);

            if (result.Error == ServiceError.NotFound) return NotFound();

            await _icons.DeleteStoredAsync(result.Value!);

            TempData["SuccessMessage"] = $"Библиотека {result.Value!.Name} е премахната от каталога.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Admin/Libraries/RefreshIcon/5
        // Downloads the site icon again, for example after the site changed its logo
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefreshIcon(int id)
        {
            var result = await _icons.RefreshAsync(id, force: true);
            if (result.Error == ServiceError.NotFound) return NotFound();

            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = $"Иконата на {result.Value!.Name} е обновена.";
            }
            else
            {
                TempData["ErrorMessage"] = result.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // The library is already saved: a site without a usable icon only adds a note to the message
        private async Task<string> DownloadIconAsync(int id)
        {
            var icon = await _icons.RefreshAsync(id, cancellationToken: HttpContext?.RequestAborted ?? default);
            return icon.Succeeded ? string.Empty : " " + icon.Message;
        }

        private static OnlineLibraryFormViewModel ToForm(OnlineLibrary library) => new()
        {
            Id = library.Id,
            Name = library.Name,
            Description = library.Description,
            Url = library.Url,
            Icon = library.Icon,
            SortOrder = library.SortOrder
        };

        // The icon drop-down is filled from the service list, not from the posted form
        private OnlineLibraryFormViewModel WithIcons(OnlineLibraryFormViewModel model)
        {
            ViewBag.Icons = _libraries.GetIcons()
                .Select(i => new SelectListItem(i.Label, i.Class, i.Class == (model.Icon ?? OnlineLibrary.DefaultIcon)))
                .ToList();
            return model;
        }

        // The message already says whether the name or the address is the problem, so it goes on the form summary
        private void AddError(ServiceResult result)
        {
            ModelState.AddModelError(string.Empty, result.Message!);
        }
    }
}
