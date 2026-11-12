using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLibrary.Data.Constants;

namespace SchoolLibrary.Areas.Admin.Controllers
{
    // Base class for every controller in the Admin area: only administrators get in
    [Area("Admin")]
    [Authorize(Roles = Roles.Administrator)]
    public abstract class AdminController : Controller
    {
    }
}
