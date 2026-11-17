using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.Discovery;
using SchoolLibrary.Services.GradeLevels;
using SchoolLibrary.ViewModels;
using System.Security.Claims;

namespace SchoolLibrary.Controllers
{
    // "Find resources": teachers and administrators search the trusted sources for a topic and add what they pick
    [Authorize(Roles = Roles.TeacherOrAdministrator)]
    public class DiscoveryController : Controller
    {
        private readonly IResourceDiscoveryService _discovery;
        private readonly ICategoryService _categories;
        private readonly IGradeLevelService _gradeLevels;

        public DiscoveryController(IResourceDiscoveryService discovery, ICategoryService categories, IGradeLevelService gradeLevels)
        {
            _discovery = discovery;
            _categories = categories;
            _gradeLevels = gradeLevels;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // GET: Discovery?topic=...&sources=...
        // A search is a read-only GET, so the address can be shared and the back button works
        [HttpGet]
        public async Task<IActionResult> Index(string? topic, string[]? sources, CancellationToken cancellationToken)
        {
            var allSources = _discovery.GetSources().ToList();
            var known = allSources.Select(s => s.Key).ToHashSet();
            var chosen = (sources ?? Array.Empty<string>()).Where(known.Contains).Distinct().ToList();

            var vm = new DiscoveryIndexViewModel
            {
                Topic = topic,
                Sources = allSources,
                // Nothing ticked (first visit) means all sources
                SelectedSources = chosen.Count > 0 ? chosen : known.ToList()
            };

            // The first visit shows only the form
            if (string.IsNullOrWhiteSpace(topic))
            {
                return View(vm);
            }

            var result = await _discovery.SearchAsync(topic, chosen, cancellationToken);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(nameof(vm.Topic), result.Message!);
                return View(vm);
            }

            vm.Searched = true;
            vm.Candidates = result.Value!.Candidates.Select(CandidateRow.From).ToList();
            vm.FailedSources = result.Value.FailedSources.ToList();
            vm.SearchedTerms = result.Value.Terms?.ToList() ?? new List<string>();
            vm.AllCategories = (await _categories.GetOptionsAsync()).ToList();
            vm.AllGradeLevels = (await _gradeLevels.GetAllAsync()).ToList();
            return View(vm);
        }

        // POST: Discovery/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(DiscoveryAddViewModel vm)
        {
            var selected = vm.Candidates.Where(c => c.IsSelected).Select(c => c.ToSelection()).ToList();

            var result = await _discovery.AddAsync(selected, vm.SelectedCategoryIds, vm.SelectedGradeLevelIds, CurrentUserId);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = result.Message;
            }
            else
            {
                if (result.Value!.Added.Count > 0)
                {
                    TempData["SuccessMessage"] = $"Добавени ресурси: {result.Value.Added.Count}.";
                }

                if (result.Value.Skipped.Count > 0)
                {
                    TempData["ErrorMessage"] = "Не са добавени: " + string.Join(" ", result.Value.Skipped);
                }
            }

            // Back to the same search: what was just added now shows as already in the library
            return RedirectToAction(nameof(Index), new { topic = vm.Topic, sources = vm.Sources });
        }
    }
}
