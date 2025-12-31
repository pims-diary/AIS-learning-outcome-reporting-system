using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LOARS.Web.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class LecturerDashboardController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
