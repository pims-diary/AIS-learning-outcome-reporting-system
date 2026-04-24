using System.Diagnostics;
using AIS_LO_System.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIS_LO_System.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [AllowAnonymous]
        [HttpGet("Home/StatusCode/{code:int}")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult StatusCodePage(int code)
        {
            Response.StatusCode = code;

            ViewBag.StatusCode = code;
            ViewBag.StatusTitle = code switch
            {
                403 => "Access Denied",
                404 => "Page Not Found",
                401 => "Sign In Required",
                _ => "Request Could Not Be Completed"
            };
            ViewBag.StatusMessage = code switch
            {
                403 => "You do not have permission to access this page or perform this action.",
                404 => "The page or resource you requested could not be found. It may have been moved, removed, or the URL may be incorrect.",
                401 => "You need to sign in before you can access this page.",
                _ => "The server returned an unexpected response for this request. Please go back or try again from a safe page."
            };

            return View("StatusCode");
        }
    }
}