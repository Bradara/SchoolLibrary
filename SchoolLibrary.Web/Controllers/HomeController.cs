using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Models;
using SchoolLibrary.Services.Favorites;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.ViewModels;
using System.Security.Claims;
using System.Diagnostics;

namespace SchoolLibrary.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        // The services come with the action, so the error pages keep their plain constructor
        public async Task<IActionResult> Index(
            [FromServices] IResourceService resources,
            [FromServices] IFavoriteService favorites)
        {
            var newest = await resources.SearchAsync(new ResourceFilter
            {
                PageSize = HomeIndexViewModel.NewestCount,
                Viewer = ResourceViewer.For(User)
            });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var favoriteIds = userId != null
                ? await favorites.GetFavoriteIdsAsync(userId, newest.Items.Select(r => r.Id))
                : new HashSet<int>();

            return View(new HomeIndexViewModel
            {
                Newest = newest.Items,
                FavoriteIds = favoriteIds,
                TotalCount = newest.TotalCount
            });
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult About()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        // Reached by re-execution from the status code pages ("?code=404") and from the exception handler
        // (no code: a server error). The response keeps the status code of the original request.
        public IActionResult Error(int? code)
        {
            var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            return View(ErrorViewModel.For(code, requestId));
        }
    }
}
