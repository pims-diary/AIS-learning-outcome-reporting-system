using System.Security.Claims;
using AIS_LO_System.Data;
using AIS_LO_System.Models;
using LOARS.Web.Models.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BC = BCrypt.Net.BCrypt;

namespace LOARS.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin")) return RedirectToAction("Dashboard", "Admin");
                return RedirectToAction("Index", "LecturerDashboard");
            }
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

            var user = await _context.AppUsers
                .FirstOrDefaultAsync(u => u.Username == model.Username.Trim().ToLower());

            bool valid = user != null && BC.Verify(model.Password, user.PasswordHash);

            if (!valid)
            {
                TempData["Error"] = "Invalid username or password.";
                return View(model);
            }

            if (!user!.IsActive)
            {
                TempData["Error"] = "Your account has been deactivated. Please contact an administrator.";
                return View(model);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user!.Username),
                new Claim(ClaimTypes.GivenName, user.FullName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("UserId", user.Id.ToString())
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            TempData["Success"] = "Login successful.";

            // Redirect based on role
            return user.Role switch
            {
                UserRole.Admin => RedirectToAction("Dashboard", "Admin"),
                _ => RedirectToAction("Index", "LecturerDashboard")
            };
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

        // Callable from any layout while impersonating — must live here (no class-level role restriction)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> StopImpersonating()
        {
            var adminUsername = User.FindFirst("ImpersonatedBy")?.Value;
            if (string.IsNullOrEmpty(adminUsername))
                return RedirectToAction("Dashboard", "Admin");

            var admin = await _context.AppUsers.FirstOrDefaultAsync(u => u.Username == adminUsername);
            if (admin == null)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return RedirectToAction("Login");
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name,      admin.Username),
                new(ClaimTypes.GivenName, admin.FullName),
                new(ClaimTypes.Role,      admin.Role.ToString()),
                new("UserId", admin.Id.ToString())
                // No ImpersonatedBy — clean admin session
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            TempData["Success"] = "Returned to admin session.";
            return RedirectToAction("Users", "Admin");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}