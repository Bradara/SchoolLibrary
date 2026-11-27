using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.Services.Statistics;

namespace SchoolLibrary.Areas.Admin.Controllers
{
    public class HomeController : AdminController
    {
        private readonly IStatisticsService _statistics;

        public HomeController(IStatisticsService statistics)
        {
            _statistics = statistics;
        }

        // GET: Admin
        public async Task<IActionResult> Index([FromServices] IResourceCoverService covers)
        {
            // The books uploaded before covers were kept; the dashboard offers to take their covers out
            ViewData["BooksWithoutCover"] = await covers.CountBooksWithoutCoverAsync();
            ViewData["PdfsWithoutCover"] = await covers.CountPdfsWithoutCoverAsync();
            return View(await _statistics.GetDashboardAsync());
        }
    }
}
