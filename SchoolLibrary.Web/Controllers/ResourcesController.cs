using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.GradeLevels;
using SchoolLibrary.Services.Licenses;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.ViewModels;
using System.Security.Claims;

namespace SchoolLibrary.Controllers
{
    [Authorize]
    public class ResourcesController : Controller
    {
        private readonly IResourceService _resources;
        private readonly ICategoryService _categories;
        private readonly IGradeLevelService _gradeLevels;
        private readonly ILicenseService _licenses;

        public ResourcesController(
            IResourceService resources,
            ICategoryService categories,
            IGradeLevelService gradeLevels,
            ILicenseService licenses)
        {
            _resources = resources;
            _categories = categories;
            _gradeLevels = gradeLevels;
            _licenses = licenses;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // GET: Resources
        [AllowAnonymous]
        public async Task<IActionResult> Index(
            ResourceKind? kind,
            string? searchTitle,
            int? categoryId,
            int? gradeLevelId,
            string? ownerId,
            DateTime? fromDate,
            DateTime? toDate,
            ResourceSortOrder? sortOrder,
            int page = 1)
        {
            var result = await _resources.SearchAsync(new ResourceFilter
            {
                Kind = kind,
                SearchText = searchTitle,
                CategoryId = categoryId,
                GradeLevelId = gradeLevelId,
                OwnerId = ownerId,
                FromDate = fromDate,
                ToDate = toDate,
                SortOrder = sortOrder ?? ResourceSortOrder.DateDesc,
                Page = page
            });

            var owners = await _resources.GetOwnersAsync();

            var vm = new ResourceIndexViewModel
            {
                Kind = kind,
                SearchTitle = searchTitle,
                CategoryId = categoryId,
                GradeLevelId = gradeLevelId,
                OwnerId = ownerId,
                FromDate = fromDate,
                ToDate = toDate,
                SortOrder = sortOrder ?? ResourceSortOrder.DateDesc,

                Categories = await _categories.GetOptionsAsync(),
                GradeLevels = await _gradeLevels.GetAllAsync(),
                Owners = owners.Select(o => new SelectListItem { Value = o.Id, Text = o.FullName }).ToList(),

                Resources = result.Items,
                Page = result.Page,
                TotalPages = result.TotalPages,
                TotalCount = result.TotalCount
            };

            return View(vm);
        }

        // GET: Resources/Details
        [AllowAnonymous]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var resource = await _resources.GetDetailsAsync(id.Value);
            if (resource == null)
            {
                return NotFound();
            }

            return View(resource);
        }

        // GET: Resources/Create
        [Authorize(Roles = Roles.TeacherOrAdministrator)]
        public async Task<IActionResult> Create(ResourceKind kind = ResourceKind.Lesson)
        {
            var vm = new ResourceFormViewModel { Kind = kind };
            await LoadOptionsAsync(vm);
            return View(vm);
        }

        // POST: Resources/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = Roles.TeacherOrAdministrator)]
        public async Task<IActionResult> Create(ResourceFormViewModel vm)
        {
            ValidateSelections(vm);

            if (ModelState.IsValid)
            {
                var result = await _resources.CreateAsync(ToInput(vm), CurrentUserId);
                if (result.Succeeded)
                {
                    TempData["SuccessMessage"] = $"Ресурсът {result.Value!.Title} е създаден успешно.";
                    return RedirectToAction(nameof(Index));
                }

                ModelState.AddModelError(string.Empty, result.Message!);
            }

            await LoadOptionsAsync(vm);
            return View(vm);
        }

        // GET: Resources/Edit
        [Authorize(Roles = Roles.TeacherOrAdministrator)]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var result = await _resources.GetForEditAsync(id.Value, CurrentUserId);
            if (!result.Succeeded)
            {
                return ToActionResult(result);
            }

            var resource = result.Value!;
            var vm = new ResourceFormViewModel
            {
                Id = resource.Id,
                Title = resource.Title,
                Author = resource.Author,
                Source = resource.Source,
                Description = resource.Description,
                Url = resource.Url,
                Kind = resource.Kind,
                LicenseId = resource.LicenseId,
                SelectedCategoryIds = resource.Categories.Select(c => c.Id).ToList(),
                SelectedGradeLevelIds = resource.GradeLevels.Select(g => g.Id).ToList()
            };
            await LoadOptionsAsync(vm);
            return View(vm);
        }

        // POST: Resources/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = Roles.TeacherOrAdministrator)]
        public async Task<IActionResult> Edit(int id, ResourceFormViewModel vm)
        {
            if (id != vm.Id)
            {
                return NotFound();
            }

            ValidateSelections(vm);

            if (ModelState.IsValid)
            {
                var result = await _resources.UpdateAsync(id, ToInput(vm), CurrentUserId);
                if (result.Succeeded)
                {
                    TempData["SuccessMessage"] = $"Ресурсът {result.Value!.Title} е редактиран успешно.";
                    return RedirectToAction(nameof(Index));
                }

                if (result.Error != ServiceError.Invalid)
                {
                    return ToActionResult(result);
                }

                ModelState.AddModelError(string.Empty, result.Message!);
            }

            await LoadOptionsAsync(vm);
            return View(vm);
        }

        // GET: Resources/Delete
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var result = await _resources.GetForDeleteAsync(id.Value, CurrentUserId, User.IsInRole(Roles.Administrator));
            if (!result.Succeeded)
            {
                return ToActionResult(result);
            }

            return View(result.Value);
        }

        // POST: Resources/Delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var result = await _resources.DeleteAsync(id, CurrentUserId, User.IsInRole(Roles.Administrator));
            if (!result.Succeeded)
            {
                return ToActionResult(result);
            }

            TempData["SuccessMessage"] = $"Ресурсът {result.Value!.Title} е изтрит успешно.";
            return RedirectToAction(nameof(Index));
        }

        // Maps a failed service result to the matching HTTP response
        private IActionResult ToActionResult(ServiceResult result) => result.Error switch
        {
            ServiceError.NotFound => NotFound(),
            ServiceError.Forbidden => Forbid(),
            _ => BadRequest(result.Message)
        };

        private static ResourceInput ToInput(ResourceFormViewModel vm) => new()
        {
            Title = vm.Title,
            Author = vm.Author,
            Source = vm.Source,
            Description = vm.Description,
            Url = vm.Url,
            Kind = vm.Kind,
            LicenseId = vm.LicenseId,
            CategoryIds = vm.SelectedCategoryIds,
            GradeLevelIds = vm.SelectedGradeLevelIds
        };

        private async Task LoadOptionsAsync(ResourceFormViewModel vm)
        {
            vm.AllCategories = (await _categories.GetOptionsAsync()).ToList();
            vm.AllGradeLevels = (await _gradeLevels.GetAllAsync()).ToList();
            vm.AllLicenses = (await _licenses.GetOptionsAsync()).ToList();
        }

        // Keeps the error next to the checkbox groups; the service validates the same rules again
        private void ValidateSelections(ResourceFormViewModel vm)
        {
            if (vm.SelectedCategoryIds.Count == 0)
            {
                ModelState.AddModelError(nameof(vm.SelectedCategoryIds), "Изберете поне един предмет.");
            }
            if (vm.SelectedGradeLevelIds.Count == 0)
            {
                ModelState.AddModelError(nameof(vm.SelectedGradeLevelIds), "Изберете поне един клас.");
            }
        }
    }
}
