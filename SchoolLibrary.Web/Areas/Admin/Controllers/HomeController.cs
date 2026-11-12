using Microsoft.AspNetCore.Mvc;
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
        public async Task<IActionResult> Index()
        {
            return View(await _statistics.GetDashboardAsync());
        }
    }
}
