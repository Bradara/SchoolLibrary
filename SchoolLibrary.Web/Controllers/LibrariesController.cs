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

        public LibrariesController(IOnlineLibraryService libraries)
        {
            _libraries = libraries;
        }

        // GET: Libraries
        public async Task<IActionResult> Index()
        {
            return View(await _libraries.GetAllAsync());
        }
    }
}
