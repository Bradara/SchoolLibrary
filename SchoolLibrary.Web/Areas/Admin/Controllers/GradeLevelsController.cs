using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Services.GradeLevels;

namespace SchoolLibrary.Areas.Admin.Controllers
{
    // The grades (5th to 12th) are a fixed set seeded from the Grades enum, so this page only shows them
    public class GradeLevelsController : AdminController
    {
        private readonly IGradeLevelService _gradeLevels;

        public GradeLevelsController(IGradeLevelService gradeLevels)
        {
            _gradeLevels = gradeLevels;
        }

        // GET: Admin/GradeLevels
        public async Task<IActionResult> Index()
        {
            return View(await _gradeLevels.GetAllWithCountsAsync());
        }
    }
}
