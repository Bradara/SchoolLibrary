using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Favorites;
using SchoolLibrary.ViewModels;
using System.Security.Claims;

namespace SchoolLibrary.Controllers
{
    // Every signed-in user (student, parent, teacher, administrator) can keep favorites
    [Authorize]
    public class FavoritesController : Controller
    {
        private readonly IFavoriteService _favorites;

        public FavoritesController(IFavoriteService favorites)
        {
            _favorites = favorites;
        }

        private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // GET: Favorites
        public async Task<IActionResult> Index(int page = 1)
        {
            var result = await _favorites.GetFavoritesAsync(CurrentUserId, page);

            return View(new FavoritesIndexViewModel
            {
                Resources = result.Items,
                Page = result.Page,
                TotalPages = result.TotalPages,
                TotalCount = result.TotalCount
            });
        }

        // POST: Favorites/Add
        // The heart is a plain form post with a redirect back to the page it was clicked on.
        // Later optimization: send it with fetch() and swap the icon, so the page does not reload.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int resourceId, string? returnUrl)
        {
            var result = await _favorites.AddAsync(CurrentUserId, resourceId);
            if (result.Error == ServiceError.NotFound)
            {
                return NotFound();
            }

            return RedirectBack(returnUrl);
        }

        // POST: Favorites/Remove
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int resourceId, string? returnUrl)
        {
            await _favorites.RemoveAsync(CurrentUserId, resourceId);
            return RedirectBack(returnUrl);
        }

        // Only addresses of this site are followed, so the link cannot send the user elsewhere
        private IActionResult RedirectBack(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
