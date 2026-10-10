using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    DateTime? toDate)
        {
            // Строй query условно — всеки филтър е optional
            var query = _context.Resources
                .Include(r => r.Category)
                .Include(r => r.GradeLevel)
                .Include(r => r.Owner)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTitle))
            {
                query = query.Where(r => r.Title.Contains(searchTitle));
            }

            if (categoryId.HasValue)
            {
                query = query.Where(r => r.CategoryId == categoryId.Value);
            }

            if (gradeLevelId.HasValue)
            {
                query = query.Where(r => r.GradeLevelId == gradeLevelId.Value);
            }

            if (!string.IsNullOrEmpty(ownerId))
            {
                query = query.Where(r => r.OwnerId == ownerId);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(r => r.CreatedOn >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                // +1 ден за да включва цялата крайна дата
                var endOfDay = toDate.Value.AddDays(1);
                query = query.Where(r => r.CreatedOn < endOfDay);
            }

            var vm = new ResourceIndexViewModel
            {
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
                        Text = u.Email!
                    })
                    .ToListAsync(),

                Resources = await query
                    .OrderByDescending(r => r.CreatedOn)
                    .ToListAsync()
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
                .Include(r => r.Category)
                .Include(r => r.GradeLevel)
                .Include(r => r.Owner)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (resource == null)
            {
                return NotFound();
            }

            return View(resource);
        }

        // GET: Resources/Create
        public IActionResult Create()
        {
            PopulateDropdowns();
            return View();
        }

        // POST: Resources/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Title,Description,Url,CategoryId,GradeLevelId")] Resource resource)
        {
            if (ModelState.IsValid)
            {
                resource.OwnerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
                resource.CreatedOn = DateTime.UtcNow;

                _context.Add(resource);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Ресурсът {resource.Title} е създаден успешно.";
                return RedirectToAction(nameof(Index));
            }

            PopulateDropdowns(resource.CategoryId, resource.GradeLevelId);
            return View(resource);
        }

        // GET: Resources/Edit
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var resource = await _context.Resources.FindAsync(id);
            if (resource == null)
            {
                return NotFound();
            }

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (resource.OwnerId != currentUserId)
            {
                return Forbid();
            }

            PopulateDropdowns(resource.CategoryId, resource.GradeLevelId);
            return View(resource);
        }

        // POST: Resources/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Title,Description,Url,CategoryId,GradeLevelId")] Resource resource)
        {
            if (id != resource.Id)
            {
                return NotFound();
            }

            var existing = await _context.Resources.FindAsync(id);
            if (existing == null)
            {
                return NotFound();
            }

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (existing.OwnerId != currentUserId)
            {
                return Forbid();
            }

            if (ModelState.IsValid)
            {
                existing.Title = resource.Title;
                existing.Description = resource.Description;
                existing.Url = resource.Url;
                existing.CategoryId = resource.CategoryId;
                existing.GradeLevelId = resource.GradeLevelId;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ResourceExists(resource.Id))
                    {
                        return NotFound();
                    }
                    throw;
                }

                TempData["SuccessMessage"] = $"Ресурсът {resource.Title} е редактиран успешно.";
                return RedirectToAction(nameof(Index));
            }

            PopulateDropdowns(resource.CategoryId, resource.GradeLevelId);
            return View(resource);
        }

        // GET: Resources/Delete
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var resource = await _context.Resources
                .Include(r => r.Category)
                .Include(r => r.GradeLevel)
                .Include(r => r.Owner)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (resource == null)
            {
                return NotFound();
            }

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (resource.OwnerId != currentUserId)
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
            if (resource.OwnerId != currentUserId)
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

        private void PopulateDropdowns(int? selectedCategoryId = null, int? selectedGradeId = null)
        {
            ViewData["CategoryId"] = new SelectList(_context.Categories, "Id", "Name", selectedCategoryId);
            ViewData["GradeLevelId"] = new SelectList(_context.GradeLevels, "Id", "Name", selectedGradeId);
        }
    }
}