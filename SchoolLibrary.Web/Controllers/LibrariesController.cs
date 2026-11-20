using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Services.OnlineLibraries;

namespace SchoolLibrary.Controllers
{
    // The catalog of online libraries for signed-in users: cards with an icon, a short description and a link
    [Authorize]
    public class LibrariesController : Controller
    {
        private readonly IOnlineLibraryService _libraries;
        private readonly ILibraryIconService _icons;

        public LibrariesController(IOnlineLibraryService libraries, ILibraryIconService icons)
        {
            _libraries = libraries;
            _icons = icons;
        }

        // GET: Libraries
        public async Task<IActionResult> Index()
        {
            return View(await _libraries.GetAllAsync());
        }

        // GET: Libraries/Icon/5?v=...
        // The site icon is served from this site, so the browsers of the visitors never contact the library's site.
        // The address carries the storage key ("v"), so a new icon gets a new address and the old one can be cached long.
        [ResponseCache(Duration = 7 * 24 * 60 * 60, Location = ResponseCacheLocation.Client)]
        public async Task<IActionResult> Icon(int id, CancellationToken cancellationToken)
        {
            var icon = await _icons.OpenAsync(id, cancellationToken);
            if (icon == null) return NotFound();

            return File(icon.Content, icon.ContentType);
        }
    }
}
