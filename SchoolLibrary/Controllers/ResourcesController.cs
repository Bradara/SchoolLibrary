using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.ViewModels;
using System.Security.Claims;

namespace SchoolLibrary.Controllers
{
    [Authorize]
    public class ResourcesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ResourcesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Resources
        [AllowAnonymous]
        public async Task<IActionResult> Index(
    string? searchTitle,
    int? categoryId,
    int? gradeLevelId,
    string? ownerId,
    DateTime? fromDate,
    DateTime? toDate,
    string? sortOrder)
        {
            // Строй query условно — всеки филтър е optional
            var query = _context.Resources
                .Include(r => r.Categories)
                .Include(r => r.GradeLevels)
                .Include(r => r.Owner)
                .AsSplitQuery()   // avoids a cartesian explosion with two collection includes
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTitle))
            {
                // ILike: case-insensitive search (PostgreSQL LIKE is case-sensitive)
                var pattern = $"%{searchTitle}%";
                query = query.Where(r => EF.Functions.ILike(r.Title, pattern) || EF.Functions.ILike(r.Author, pattern));
            }

            if (categoryId.HasValue)
            {
                query = query.Where(r => r.Categories.Any(c => c.Id == categoryId.Value));
            }

            if (gradeLevelId.HasValue)
            {
                query = query.Where(r => r.GradeLevels.Any(g => g.Id == gradeLevelId.Value));
            }

            if (!string.IsNullOrEmpty(ownerId))
            {
                query = query.Where(r => r.OwnerId == ownerId);
            }

            if (fromDate.HasValue)
            {
                // Npgsql (timestamptz) accepts only UTC DateTime values
                var from = DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
                query = query.Where(r => r.CreatedOn >= from);
            }

            if (toDate.HasValue)
            {
                // +1 ден за да включва цялата крайна дата
                var endOfDay = DateTime.SpecifyKind(toDate.Value.AddDays(1), DateTimeKind.Utc);
                query = query.Where(r => r.CreatedOn < endOfDay);
            }

            // Whitelisted sort keys (anything else falls back to newest first).
            // ToLower() makes title/author ordering case-insensitive.
            query = sortOrder switch
            {
                "date_asc" => query.OrderBy(r => r.CreatedOn),
                "title_asc" => query.OrderBy(r => r.Title.ToLower()).ThenByDescending(r => r.CreatedOn),
                "title_desc" => query.OrderByDescending(r => r.Title.ToLower()).ThenByDescending(r => r.CreatedOn),
                "author_asc" => query.OrderBy(r => r.Author.ToLower()).ThenByDescending(r => r.CreatedOn),
                "author_desc" => query.OrderByDescending(r => r.Author.ToLower()).ThenByDescending(r => r.CreatedOn),
                "owner_asc" => query.OrderBy(r => r.Owner!.FirstName).ThenBy(r => r.Owner!.LastName).ThenByDescending(r => r.CreatedOn),
                _ => query.OrderByDescending(r => r.CreatedOn)
            };

            var vm = new ResourceIndexViewModel
            {
                SortOrder = sortOrder,
                SearchTitle = searchTitle,
                CategoryId = categoryId,
                GradeLevelId = gradeLevelId,
                OwnerId = ownerId,
                FromDate = fromDate,
                ToDate = toDate,

                Categories = await _context.Categories
                    .OrderBy(c => c.Name)
                    .ToListAsync(),

                GradeLevels = await _context.GradeLevels
                    .OrderBy(g => g.Number)
                    .ToListAsync(),

                Owners = await _context.Users
                    .Where(u => _context.Resources.Any(r => r.OwnerId == u.Id))
                    .OrderBy(u => u.Email)
                    .Select(u => new SelectListItem
                    {
                        Value = u.Id,
                        Text = u.FirstName + " " + u.LastName
                    })
                    .ToListAsync(),

                Resources = await query.ToListAsync()
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

            var resource = await _context.Resources
                .Include(r => r.Categories)
                .Include(r => r.GradeLevels)
                .Include(r => r.Owner)
                .AsSplitQuery()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (resource == null)
            {
                return NotFound();
            }

            return View(resource);
        }

        // GET: Resources/Create
        public async Task<IActionResult> Create()
        {
            var vm = new ResourceFormViewModel();
            await LoadOptionsAsync(vm);
            return View(vm);
        }

        // POST: Resources/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ResourceFormViewModel vm)
        {
            ValidateSelections(vm);

            if (ModelState.IsValid)
            {
                var resource = new Resource
                {
                    OwnerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                    CreatedOn = DateTime.UtcNow
                };
                ApplyFormToResource(vm, resource);
                resource.Categories = await _context.Categories
                    .Where(c => vm.SelectedCategoryIds.Contains(c.Id)).ToListAsync();
                resource.GradeLevels = await _context.GradeLevels
                    .Where(g => vm.SelectedGradeLevelIds.Contains(g.Id)).ToListAsync();

                _context.Add(resource);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Ресурсът {resource.Title} е създаден успешно.";
                return RedirectToAction(nameof(Index));
            }

            await LoadOptionsAsync(vm);
            return View(vm);
        }

        // GET: Resources/Edit
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var resource = await _context.Resources
                .Include(r => r.Categories)
                .Include(r => r.GradeLevels)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == id);
            if (resource == null)
            {
                return NotFound();
            }

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (resource.OwnerId != currentUserId)
            {
                return Forbid();
            }

            var vm = new ResourceFormViewModel
            {
                Id = resource.Id,
                Title = resource.Title,
                Author = resource.Author,
                Source = resource.Source,
                Description = resource.Description,
                Url = resource.Url,
                SelectedCategoryIds = resource.Categories.Select(c => c.Id).ToList(),
                SelectedGradeLevelIds = resource.GradeLevels.Select(g => g.Id).ToList()
            };
            await LoadOptionsAsync(vm);
            return View(vm);
        }

        // POST: Resources/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ResourceFormViewModel vm)
        {
            if (id != vm.Id)
            {
                return NotFound();
            }

            var existing = await _context.Resources
                .Include(r => r.Categories)
                .Include(r => r.GradeLevels)
                .AsSplitQuery()
                .FirstOrDefaultAsync(r => r.Id == id);
            if (existing == null)
            {
                return NotFound();
            }

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (existing.OwnerId != currentUserId)
            {
                return Forbid();
            }

            ValidateSelections(vm);

            if (ModelState.IsValid)
            {
                ApplyFormToResource(vm, existing);

                // Replace the selections: EF removes/adds rows in the join tables
                existing.Categories.Clear();
                foreach (var c in await _context.Categories.Where(c => vm.SelectedCategoryIds.Contains(c.Id)).ToListAsync())
                {
                    existing.Categories.Add(c);
                }
                existing.GradeLevels.Clear();
                foreach (var g in await _context.GradeLevels.Where(g => vm.SelectedGradeLevelIds.Contains(g.Id)).ToListAsync())
                {
                    existing.GradeLevels.Add(g);
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ResourceExists(vm.Id))
                    {
                        return NotFound();
                    }
                    throw;
                }

                TempData["SuccessMessage"] = $"Ресурсът {existing.Title} е редактиран успешно.";
                return RedirectToAction(nameof(Index));
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

            var resource = await _context.Resources
                .Include(r => r.Categories)
                .Include(r => r.GradeLevels)
                .Include(r => r.Owner)
                .AsSplitQuery()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (resource == null)
            {
                return NotFound();
            }

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (resource.OwnerId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            return View(resource);
        }

        // POST: Resources/Delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var resource = await _context.Resources.FindAsync(id);
            if (resource == null)
            {
                return NotFound();
            }

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (resource.OwnerId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            _context.Resources.Remove(resource);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Ресурсът {resource.Title} е изтрит успешно.";
            return RedirectToAction(nameof(Index));
        }

        private bool ResourceExists(int id)
        {
            return _context.Resources.Any(e => e.Id == id);
        }

        private async Task LoadOptionsAsync(ResourceFormViewModel vm)
        {
            vm.AllCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
            vm.AllGradeLevels = await _context.GradeLevels.OrderBy(g => g.Number).ToListAsync();
        }

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

        private static void ApplyFormToResource(ResourceFormViewModel vm, Resource resource)
        {
            resource.Title = vm.Title;
            resource.Description = vm.Description ?? string.Empty;
            resource.Url = vm.Url;
            resource.Author = vm.Author;
            resource.Source = vm.Source;
        }
    }
}
