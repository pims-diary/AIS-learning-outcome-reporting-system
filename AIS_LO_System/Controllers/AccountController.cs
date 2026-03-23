using System.Security.Claims;
using LOARS.Web.Models.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LOARS.Web.Controllers
{
    public class AccountController : Controller
    {
        // ✅ Temporary demo credentials (replace later with DB/Identity)
        private const string LecturerUsername = "lecturer";
        private const string LecturerPassword = "Password@123";

        [HttpGet]
        public IActionResult Login()
        {
            // If already logged in → go dashboard
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "LecturerDashboard");

            return View(new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please enter username and password.";
                return View(model);
            }

            // Basic credential check (for now)
            var isValid = model.Username == LecturerUsername && model.Password == LecturerPassword;

            if (!isValid)
            {
                TempData["Error"] = "Invalid username or password.";
                return View(model);
            }

            // Create lecturer identity + role
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, model.Username),
                new Claim(ClaimTypes.Role, "Lecturer")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            TempData["Success"] = "Login successful.";
            return RedirectToAction("Index", "LecturerDashboard");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Success"] = "You have been logged out.";
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
