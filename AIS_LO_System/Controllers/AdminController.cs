using AIS_LO_System.Data;
using AIS_LO_System.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BC = BCrypt.Net.BCrypt;

namespace AIS_LO_System.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public AdminController(ApplicationDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // =============================================
        // DASHBOARD
        // =============================================
        public async Task<IActionResult> Dashboard()
        {
            var now = DateTime.Now;
            int currentYear = now.Year;
            int currentTrimester = now.Month <= 4 ? 1 : now.Month <= 8 ? 2 : 3;

            ViewBag.TotalCourses = await _context.Courses.CountAsync();
            ViewBag.TotalLecturers = await _context.AppUsers.CountAsync(u => u.Role == UserRole.Lecturer);
            ViewBag.TotalModerators = await _context.Courses.CountAsync(c => c.ModeratorId != null);
            ViewBag.TotalStudents = await _context.Students.CountAsync();

            // Current trimester stats
            ViewBag.ActiveCourses = await _context.Courses
                .CountAsync(c => c.Year == currentYear && c.Trimester == currentTrimester);
            ViewBag.CoursesWithoutLecturer = await _context.Courses
                .CountAsync(c => c.Year == currentYear && c.Trimester == currentTrimester && c.LecturerId == null);
            ViewBag.CoursesWithRubric = await _context.Rubrics
                .Select(r => r.Assignment.CourseCode)
                .Distinct()
                .CountAsync();
            ViewBag.CoursesWithLO = await _context.LearningOutcomes
                .Select(lo => lo.CourseCode)
                .Distinct()
                .CountAsync();

            // Recently added rubrics (real activity) — materialise first, then project to avoid anonymous type in ViewBag
            var recentRubricData = await _context.Rubrics
                .Include(r => r.Assignment)
                .OrderByDescending(r => r.CreatedDate)
                .Take(5)
                .ToListAsync();

            ViewBag.RecentRubrics = recentRubricData
                .Select(r => new RecentRubricItem
                {
                    CourseCode = r.Assignment?.CourseCode ?? "—",
                    AssessmentName = r.Assignment?.AssessmentName ?? "—",
                    CreatedDate = r.CreatedDate
                })
                .ToList();

            ViewBag.CurrentYear = currentYear;
            ViewBag.CurrentTrimester = currentTrimester;

            return View();
        }

        // =============================================
        // COURSES
        // =============================================
        public async Task<IActionResult> Courses(string? search, string? trimester, string? status, string? school)
        {
            var now = DateTime.Now;
            // Determine current trimester from month: T1=Jan-Apr, T2=May-Aug, T3=Sep-Dec
            int currentYear = now.Year;
            int currentTrimester = now.Month <= 4 ? 1 : now.Month <= 8 ? 2 : 3;

            var query = _context.Courses
                .Include(c => c.Lecturer)
                .Include(c => c.Moderator)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.Code.Contains(search) || c.Title.Contains(search));

            if (!string.IsNullOrWhiteSpace(trimester) && trimester != "all")
            {
                var parts = trimester.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out int yr) && int.TryParse(parts[1], out int tri))
                    query = query.Where(c => c.Year == yr && c.Trimester == tri);
            }

            if (!string.IsNullOrWhiteSpace(school) && school != "all")
                query = query.Where(c => c.School == school);

            // Active = current year AND current trimester
            if (status == "active")
                query = query.Where(c => c.Year == currentYear && c.Trimester == currentTrimester);
            else if (status == "inactive")
                query = query.Where(c => !(c.Year == currentYear && c.Trimester == currentTrimester));

            ViewBag.Status = status ?? "all";
            ViewBag.CurrentYear = currentYear;
            ViewBag.CurrentTrimester = currentTrimester;
            ViewBag.SelectedSchool = school ?? "all";

            ViewBag.Lecturers = await _context.AppUsers.Where(u => u.Role == UserRole.Lecturer).ToListAsync();
            // Moderators are any Lecturer — moderator is a per-course assignment, not a separate role
            ViewBag.Moderators = await _context.AppUsers.Where(u => u.Role == UserRole.Lecturer).ToListAsync();
            ViewBag.Trimesters = await _context.Courses
                .Select(c => new { c.Year, c.Trimester })
                .Distinct()
                .OrderByDescending(x => x.Year).ThenBy(x => x.Trimester)
                .ToListAsync()
                .ContinueWith(t => t.Result.Select(x => new TrimesterOption { Year = x.Year, Trimester = x.Trimester }).ToList());

            return View(await query.OrderByDescending(c => c.Year).ThenBy(c => c.Trimester).ThenBy(c => c.Code).ToListAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCourse(string code, string title, int year, int trimester,
            string school, int? lecturerId, int? moderatorId)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(title))
            {
                TempData["Error"] = "Course code and title are required.";
                return RedirectToAction(nameof(Courses), "Admin");
            }

            // Note: the same lecturer CAN be assigned as moderator of their own course

            var course = new Course
            {
                Code = code.Trim().ToUpper(),
                Title = title.Trim(),
                Year = year,
                Trimester = trimester,
                School = string.IsNullOrWhiteSpace(school) ? "Information Technology" : school.Trim(),
                LecturerId = lecturerId,
                ModeratorId = moderatorId,
                CanEditLO = true,
                CanReuploadOutline = true
            };

            _context.Courses.Add(course);
            await _context.SaveChangesAsync();
            TempData["Success"] = $"Course {course.Code} added.";
            return RedirectToAction(nameof(Courses), "Admin");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCourse(int id, string title, int year, int trimester,
    string school, int? lecturerId, int? moderatorId)
        {
            var course = await _context.Courses
                .Include(c => c.LecturerEnrolments)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (course == null) return NotFound();

            course.Title = title.Trim();
            course.Year = year;
            course.Trimester = trimester;
            course.School = string.IsNullOrWhiteSpace(school) ? course.School : school.Trim();
            course.LecturerId = lecturerId;
            course.ModeratorId = moderatorId;

            // Sync LecturerCourseEnrolment: ensure lecturer and moderator both have an enrolment row.
            // Deduplicate first — if the same person is both lecturer and moderator, only one row needed.
            var enrolledUserIds = course.LecturerEnrolments.Select(e => e.UserId).ToHashSet();

            var usersToEnrol = new HashSet<int>();
            if (lecturerId.HasValue) usersToEnrol.Add(lecturerId.Value);
            if (moderatorId.HasValue) usersToEnrol.Add(moderatorId.Value);

            foreach (var userId in usersToEnrol)
            {
                if (!enrolledUserIds.Contains(userId))
                {
                    _context.LecturerCourseEnrolments.Add(new LecturerCourseEnrolment
                    {
                        UserId = userId,
                        CourseId = id
                    });
                }
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Course updated.";
            return RedirectToAction(nameof(Courses));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveCourse(int id)
        {
            var course = await _context.Courses
                .Include(c => c.StudentEnrolments)
                .Include(c => c.LecturerEnrolments)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (course != null)
            {
                // Remove student enrolments for this course
                _context.StudentCourseEnrolments.RemoveRange(course.StudentEnrolments);

                // Remove lecturer enrolments for this course
                _context.LecturerCourseEnrolments.RemoveRange(course.LecturerEnrolments);

                // Remove the course itself
                _context.Courses.Remove(course);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Course {course.Code} and all its enrolments removed.";
            }
            return RedirectToAction(nameof(Courses), "Admin");
        }

        // =============================================
        // STUDENTS
        // =============================================
        public async Task<IActionResult> Students(string? search, int? courseId, string? status)
        {
            int currentYear = DateTime.Now.Year;

            var query = _context.Students
                .Include(s => s.CourseEnrolments)
                    .ThenInclude(e => e.Course)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(s => s.FullName.Contains(search) || s.StudentId.Contains(search));

            if (courseId.HasValue)
                query = query.Where(s => s.CourseEnrolments.Any(e => e.CourseId == courseId));

            // Active = enrolled in at least one course in the current year
            if (status == "active")
                query = query.Where(s => s.CourseEnrolments.Any(e => e.Course.Year == currentYear));
            else if (status == "inactive")
                query = query.Where(s => !s.CourseEnrolments.Any(e => e.Course.Year == currentYear));

            ViewBag.Status = status ?? "all";
            ViewBag.CurrentYear = currentYear;

            ViewBag.Courses = await _context.Courses
                .OrderByDescending(c => c.Year).ThenBy(c => c.Trimester).ThenBy(c => c.Code)
                .ToListAsync();

            return View(await query.OrderBy(s => s.StudentId).ToListAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddStudent(string studentId, string fullName,
    string? programme, string status, string? statusReason, List<int> courseIds)
        {
            if (string.IsNullOrWhiteSpace(studentId) || string.IsNullOrWhiteSpace(fullName))
            {
                TempData["Error"] = "Student ID and full name are required.";
                return RedirectToAction(nameof(Students));
            }

            var existing = await _context.Students.FirstOrDefaultAsync(s => s.StudentId == studentId.Trim());
            if (existing != null)
            {
                TempData["Error"] = $"Student ID {studentId} already exists.";
                return RedirectToAction(nameof(Students));
            }

            var studentStatus = Enum.TryParse<StudentStatus>(status, out var parsedStatus)
                ? parsedStatus
                : StudentStatus.Active;

            var student = new Student
            {
                StudentId = studentId.Trim(),
                FullName = fullName.Trim(),
                Programme = string.IsNullOrWhiteSpace(programme) ? null : programme.Trim(),
                Status = studentStatus,
                StatusReason = string.IsNullOrWhiteSpace(statusReason) ? null : statusReason.Trim()
            };
            _context.Students.Add(student);
            await _context.SaveChangesAsync();

            foreach (var cid in courseIds.Distinct())
            {
                _context.StudentCourseEnrolments.Add(new StudentCourseEnrolment
                {
                    StudentId = student.Id,
                    CourseId = cid
                });
            }
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Student {fullName} added.";
            return RedirectToAction(nameof(Students));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveStudent(int id)
        {
            var student = await _context.Students.FindAsync(id);
            if (student != null)
            {
                _context.Students.Remove(student);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Student removed.";
            }
            return RedirectToAction(nameof(Students));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditStudent(int id, string fullName, string? programme,
            string status, string? statusReason, List<int> courseIds)
        {
            var student = await _context.Students
                .Include(s => s.CourseEnrolments)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (student == null) return NotFound();

            student.FullName = fullName.Trim();
            student.Programme = string.IsNullOrWhiteSpace(programme) ? null : programme.Trim();
            student.StatusReason = string.IsNullOrWhiteSpace(statusReason) ? null : statusReason.Trim();

            if (Enum.TryParse<StudentStatus>(status, out var parsedStatus))
                student.Status = parsedStatus;

            // Replace enrolments with new selection
            _context.StudentCourseEnrolments.RemoveRange(student.CourseEnrolments);
            await _context.SaveChangesAsync();

            foreach (var cid in courseIds.Distinct())
            {
                _context.StudentCourseEnrolments.Add(new StudentCourseEnrolment
                {
                    StudentId = student.Id,
                    CourseId = cid
                });
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Student {fullName} updated.";
            return RedirectToAction(nameof(Students));
        }

        // =============================================
        // USERS
        // =============================================
        public async Task<IActionResult> Users(string? search, string? role, string? status)
        {
            var query = _context.AppUsers.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(u => u.FullName.Contains(search) || u.Username.Contains(search));

            if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<UserRole>(role, out var roleEnum))
                query = query.Where(u => u.Role == roleEnum);

            // Active/Inactive filter (manual IsActive toggle)
            if (status == "active") query = query.Where(u => u.IsActive);
            if (status == "inactive") query = query.Where(u => !u.IsActive);

            ViewBag.Status = status ?? "all";

            ViewBag.Courses = await _context.Courses
                .Include(c => c.LecturerEnrolments)
                .OrderByDescending(c => c.Year).ThenBy(c => c.Code).ToListAsync();

            return View(await query.OrderBy(u => u.Role).ThenBy(u => u.FullName).ToListAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddUser(string fullName, string username,
            string? email, string password, UserRole role, List<int> courseIds)
        {
            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                TempData["Error"] = "Full name, username, and password are required.";
                return RedirectToAction(nameof(Users), "Admin");
            }

            if (await _context.AppUsers.AnyAsync(u => u.Username == username.Trim()))
            {
                TempData["Error"] = $"Username '{username}' is already taken.";
                return RedirectToAction(nameof(Users), "Admin");
            }

            var user = new AppUser
            {
                FullName = fullName.Trim(),
                Username = username.Trim().ToLower(),
                Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLower(),
                PasswordHash = BC.HashPassword(password),
                Role = role
            };

            _context.AppUsers.Add(user);
            await _context.SaveChangesAsync();

            // Assign to courses via LecturerCourseEnrolment only.
            // Course.LecturerId / ModeratorId are managed via the Courses page.
            foreach (var cid in courseIds.Distinct())
            {
                _context.LecturerCourseEnrolments.Add(new LecturerCourseEnrolment
                {
                    UserId = user.Id,
                    CourseId = cid
                });
            }
            await _context.SaveChangesAsync();

            TempData["Success"] = $"User {fullName} created.";
            return RedirectToAction(nameof(Users), "Admin");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveUser(int id)
        {
            var user = await _context.AppUsers.FindAsync(id);
            if (user != null)
            {
                if (user.Role == UserRole.Admin &&
                    await _context.AppUsers.CountAsync(u => u.Role == UserRole.Admin) <= 1)
                {
                    TempData["Error"] = "Cannot remove the last admin account.";
                    return RedirectToAction(nameof(Users), "Admin");
                }

                // Clear FK references on Courses before deleting (NoAction constraint)
                var lecturerCourses = await _context.Courses.Where(c => c.LecturerId == id).ToListAsync();
                foreach (var c in lecturerCourses) c.LecturerId = null;

                var moderatorCourses = await _context.Courses.Where(c => c.ModeratorId == id).ToListAsync();
                foreach (var c in moderatorCourses) c.ModeratorId = null;

                // Remove enrolment records
                var enrolments = await _context.LecturerCourseEnrolments.Where(e => e.UserId == id).ToListAsync();
                _context.LecturerCourseEnrolments.RemoveRange(enrolments);

                await _context.SaveChangesAsync();

                _context.AppUsers.Remove(user);
                await _context.SaveChangesAsync();
                TempData["Success"] = "User removed.";
            }
            return RedirectToAction(nameof(Users), "Admin");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUserActive(int id)
        {
            var user = await _context.AppUsers.FindAsync(id);
            if (user == null) return NotFound();

            // Prevent deactivating the last active admin
            if (user.Role == UserRole.Admin && user.IsActive &&
                await _context.AppUsers.CountAsync(u => u.Role == UserRole.Admin && u.IsActive) <= 1)
            {
                TempData["Error"] = "Cannot deactivate the last active admin account.";
                return RedirectToAction(nameof(Users), "Admin");
            }

            user.IsActive = !user.IsActive;
            await _context.SaveChangesAsync();
            TempData["Success"] = $"{user.FullName} has been marked as {(user.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Users), "Admin");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(int id, string fullName, UserRole role,
            string? email, string? newPassword, List<int> courseIds)
        {
            var user = await _context.AppUsers.FindAsync(id);
            if (user == null) return NotFound();

            // Prevent removing admin role from the last admin
            if (user.Role == UserRole.Admin && role != UserRole.Admin &&
                await _context.AppUsers.CountAsync(u => u.Role == UserRole.Admin) <= 1)
            {
                TempData["Error"] = "Cannot change the role of the last admin account.";
                return RedirectToAction(nameof(Users), "Admin");
            }

            user.FullName = fullName.Trim();
            user.Role = role;
            user.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLower();

            if (!string.IsNullOrWhiteSpace(newPassword))
                user.PasswordHash = BC.HashPassword(newPassword);

            // Replace course enrolments only — do NOT touch Course.LecturerId or Course.ModeratorId.
            // Those FKs are managed exclusively via the Courses page (AddCourse/EditCourse).
            var existing = await _context.LecturerCourseEnrolments
                .Where(e => e.UserId == id).ToListAsync();
            _context.LecturerCourseEnrolments.RemoveRange(existing);
            await _context.SaveChangesAsync();

            foreach (var cid in courseIds.Distinct())
            {
                _context.LecturerCourseEnrolments.Add(new LecturerCourseEnrolment
                {
                    UserId = id,
                    CourseId = cid
                });
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = $"User {fullName} updated.";
            return RedirectToAction(nameof(Users), "Admin");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginAs(int id)
        {
            var target = await _context.AppUsers.FindAsync(id);
            if (target == null || target.Role == UserRole.Admin)
            {
                TempData["Error"] = "Cannot impersonate this account.";
                return RedirectToAction(nameof(Users), "Admin");
            }

            // Store the admin's username so we can restore the session later
            var adminUsername = User.Identity!.Name!;

            var claims = new List<System.Security.Claims.Claim>
            {
                new(System.Security.Claims.ClaimTypes.Name,      target.Username),
                new(System.Security.Claims.ClaimTypes.GivenName, target.FullName),
                new(System.Security.Claims.ClaimTypes.Role,      target.Role.ToString()),
                new("UserId",          target.Id.ToString()),
                new("ImpersonatedBy",  adminUsername)   // sentinel claim
            };

            var identity = new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new System.Security.Claims.ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            TempData["Info"] = $"You are now viewing as {target.FullName}. This session is read-only.";

            return RedirectToAction("Index", "LecturerDashboard");
        }


        public async Task<IActionResult> Permissions()
        {
            var courses = await _context.Courses
                .OrderByDescending(c => c.Year).ThenBy(c => c.Trimester).ThenBy(c => c.Code)
                .ToListAsync();
            return View(courses);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePermission(int courseId, string permission)
        {
            var course = await _context.Courses.FindAsync(courseId);
            if (course == null) return NotFound();

            if (permission == "CanEditLO")
                course.CanEditLO = !course.CanEditLO;
            else if (permission == "CanReuploadOutline")
                course.CanReuploadOutline = !course.CanReuploadOutline;

            await _context.SaveChangesAsync();
            return Ok(new { canEditLO = course.CanEditLO, canReuploadOutline = course.CanReuploadOutline });
        }

        // =============================================
        // CSV IMPORT — COURSES
        // =============================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportCourses(IFormFile file)
        {
            if (file == null || file.Length == 0 || !file.FileName.EndsWith(".csv"))
            {
                TempData["Error"] = "Please upload a valid .csv file.";
                return RedirectToAction(nameof(Courses), "Admin");
            }

            var lecturers = await _context.AppUsers.Where(u => u.Role == UserRole.Lecturer).ToDictionaryAsync(u => u.Username, u => u.Id);
            // Moderator lookup uses the same Lecturer pool — any Lecturer can be assigned as moderator
            var moderators = lecturers;
            var existing = await _context.Courses.Select(c => new { c.Code, c.Year, c.Trimester }).ToListAsync();

            int added = 0, updated = 0, skipped = 0;

            using var reader = new StreamReader(file.OpenReadStream());
            var header = (await reader.ReadLineAsync())!.Split(',').Select(h => h.Trim().ToLower()).ToList();

            int iCode = header.IndexOf("coursecode");
            int iTitle = header.IndexOf("coursetitle");
            int iYear = header.IndexOf("year");
            int iTri = header.IndexOf("trimester");
            int iLec = header.IndexOf("lecturerusername");
            int iMod = header.IndexOf("moderatorusername");
            int iSchool = header.IndexOf("school");

            if (iCode < 0 || iTitle < 0 || iYear < 0 || iTri < 0)
            {
                TempData["Error"] = "CSV missing required columns: CourseCode, CourseTitle, Year, Trimester.";
                return RedirectToAction(nameof(Courses), "Admin");
            }

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = line.Split(',').Select(c => c.Trim()).ToArray();

                var code = iCode < cols.Length ? cols[iCode].ToUpper() : "";
                var title = iTitle < cols.Length ? cols[iTitle] : "";
                var yearStr = iYear < cols.Length ? cols[iYear] : "";
                var triStr = iTri < cols.Length ? cols[iTri] : "";

                if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(title) ||
                    !int.TryParse(yearStr, out int yr) || !int.TryParse(triStr, out int tri) ||
                    tri < 1 || tri > 3)
                { skipped++; continue; }

                var lecUser = iLec >= 0 && iLec < cols.Length ? cols[iLec] : "";
                var modUser = iMod >= 0 && iMod < cols.Length ? cols[iMod] : "";
                var school = iSchool >= 0 && iSchool < cols.Length ? cols[iSchool].Trim() : "Information Technology";
                if (!ValidSchool(school)) school = "Information Technology";

                int? lecId = lecturers.TryGetValue(lecUser, out int lid) ? lid : null;
                int? modId = moderators.TryGetValue(modUser, out int mid) ? mid : null;

                var ex = existing.FirstOrDefault(e => e.Code == code && e.Year == yr && e.Trimester == tri);
                if (ex != null)
                {
                    var dbCourse = await _context.Courses.FirstAsync(c => c.Code == code && c.Year == yr && c.Trimester == tri);
                    dbCourse.Title = title;
                    dbCourse.LecturerId = lecId ?? dbCourse.LecturerId;
                    dbCourse.ModeratorId = modId ?? dbCourse.ModeratorId;
                    if (!string.IsNullOrWhiteSpace(school)) dbCourse.School = school;
                    updated++;
                }
                else
                {
                    _context.Courses.Add(new Course
                    {
                        Code = code,
                        Title = title,
                        Year = yr,
                        Trimester = tri,
                        School = string.IsNullOrWhiteSpace(school) ? "Information Technology" : school,
                        LecturerId = lecId,
                        ModeratorId = modId,
                        CanEditLO = true,
                        CanReuploadOutline = true
                    });
                    added++;
                }
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Import complete: {added} added, {updated} updated, {skipped} skipped.";
            return RedirectToAction(nameof(Courses), "Admin");
        }

        // =============================================
        // CSV IMPORT — STUDENTS
        // =============================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportStudents(IFormFile file)
        {
            if (file == null || file.Length == 0 || !file.FileName.EndsWith(".csv"))
            {
                TempData["Error"] = "Please upload a valid .csv file.";
                return RedirectToAction(nameof(Students), "Admin");
            }

            var courseMap = await _context.Courses.ToDictionaryAsync(c => c.Code, c => c.Id);
            var studentMap = await _context.Students.ToDictionaryAsync(s => s.StudentId, s => s.Id);
            var enrolList = await _context.StudentCourseEnrolments
                .Select(e => new { e.StudentId, e.CourseId })
                .ToListAsync();
            var enrolSet = enrolList
                .Select(e => $"{e.StudentId}|{e.CourseId}")
                .ToHashSet();

            int newStudents = 0, enrolments = 0, duplicates = 0, invalidRows = 0;

            using var reader = new StreamReader(file.OpenReadStream());
            var header = (await reader.ReadLineAsync())!.Split(',').Select(h => h.Trim().ToLower()).ToList();

            int iSid = header.IndexOf("studentid");
            int iName = header.IndexOf("fullname");
            int iCourse = header.IndexOf("coursecode");

            if (iSid < 0 || iName < 0 || iCourse < 0)
            {
                TempData["Error"] = "CSV missing required columns: StudentID, FullName, CourseCode.";
                return RedirectToAction(nameof(Students), "Admin");
            }

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = line.Split(',').Select(c => c.Trim()).ToArray();

                var sid = iSid < cols.Length ? cols[iSid] : "";
                var name = iName < cols.Length ? cols[iName] : "";
                var code = iCourse < cols.Length ? cols[iCourse].ToUpper() : "";

                if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(code))
                { invalidRows++; continue; }

                if (!courseMap.TryGetValue(code, out int courseDbId))
                { invalidRows++; continue; }

                // Upsert student
                if (!studentMap.TryGetValue(sid, out int studentDbId))
                {
                    var s = new Student { StudentId = sid, FullName = name };
                    _context.Students.Add(s);
                    await _context.SaveChangesAsync();
                    studentDbId = s.Id;
                    studentMap[sid] = studentDbId;
                    newStudents++;
                }

                // Enrol if not already enrolled
                var key = $"{studentDbId}|{courseDbId}";
                if (!enrolSet.Contains(key))
                {
                    _context.StudentCourseEnrolments.Add(new StudentCourseEnrolment
                    {
                        StudentId = studentDbId,
                        CourseId = courseDbId
                    });
                    enrolSet.Add(key);
                    enrolments++;
                }
                else duplicates++;
            }

            await _context.SaveChangesAsync();

            var msg = $"Import complete: {newStudents} new student(s), {enrolments} enrolment(s) added.";
            if (duplicates > 0)
                msg += $" {duplicates} duplicate enrolment(s) were skipped — these students are already enrolled in those courses.";
            if (invalidRows > 0)
                msg += $" {invalidRows} row(s) were skipped due to missing data or unrecognised course codes.";

            if (newStudents == 0 && enrolments == 0)
                TempData["Error"] = $"No new records were imported. " +
                    (duplicates > 0 ? $"{duplicates} duplicate(s) already exist in the system." : "") +
                    (invalidRows > 0 ? $" {invalidRows} row(s) had invalid data." : "");
            else
                TempData["Success"] = msg;

            return RedirectToAction(nameof(Students), "Admin");
        }

        private static readonly string[] Schools = {
            "Information Technology", "Business", "Tourism", "Hospitality"
        };

        private static bool ValidSchool(string school) =>
            Schools.Any(s => s.Equals(school, StringComparison.OrdinalIgnoreCase));

        [HttpGet]
        public async Task<IActionResult> GetStudentReports(int studentId, string courseCode, int year, int trimester)
        {
            var student = await _context.Students.FindAsync(studentId);
            if (student == null) return NotFound();

            var course = await _context.Courses
                .FirstOrDefaultAsync(c => c.Code == courseCode && c.Year == year && c.Trimester == trimester);
            if (course == null) return NotFound();

            // Check enrolment
            var isEnrolled = await _context.StudentCourseEnrolments
                .AnyAsync(e => e.CourseId == course.Id && e.StudentId == studentId);
            if (!isEnrolled) return NotFound();

            // Get all assignments for this course
            var assignments = await _context.Assignments
                .Where(a => a.CourseCode == courseCode && a.Year == year && a.Trimester == trimester)
                .OrderBy(a => a.AssessmentName)
                .ToListAsync();

            var reportsDir = Path.Combine(_env.WebRootPath, "uploads", "reports");

            var assessmentReports = assignments.Select(a =>
            {
                // Look for LOAchievement report file: {studentId}_{ASSESSMENTNAME}_LOAchievementReport.pdf
                var safeAssessment = a.AssessmentName.Replace(" ", "_").ToUpper();
                var loFileName = $"{student.StudentId}_{safeAssessment}_LOAchievementReport.pdf";
                var loPath = Path.Combine(reportsDir, "lo-achievement", loFileName);
                var loExists = System.IO.File.Exists(loPath);

                return new
                {
                    assessmentName = a.AssessmentName,
                    reportType = "LO Achievement Report",
                    fileName = loFileName,
                    exists = loExists,
                    downloadUrl = loExists
                        ? Url.Content($"~/uploads/reports/lo-achievement/{loFileName}")
                        : null
                };
            }).ToList();

            // Student LO Report: {studentId}_{courseCode}_{year}_T{trimester}_StudentLOReport.pdf
            var studentLoFileName = $"{student.StudentId}_{courseCode}_{year}_T{trimester}_StudentLOReport.pdf";
            var studentLoPath = Path.Combine(reportsDir, "student-lo", studentLoFileName);
            var studentLoExists = System.IO.File.Exists(studentLoPath);

            var result = new
            {
                studentId = student.StudentId,
                studentName = student.FullName,
                courseCode,
                year,
                trimester,
                assessmentReports,
                studentLoReport = new
                {
                    exists = studentLoExists,
                    fileName = studentLoFileName,
                    downloadUrl = studentLoExists
                        ? Url.Content($"~/uploads/reports/student-lo/{studentLoFileName}")
                        : null,
                    // Always allow generating via the existing controller
                    generateUrl = Url.Action("DownloadStudentCourseReportPdf", "StudentLOReport", new
                    {
                        studentId,
                        courseCode,
                        courseTitle = course.Title,
                        year,
                        trimester
                    })
                }
            };

            return Json(result);
        }
    }
}

public class TrimesterOption
{
    public int Year { get; set; }
    public int Trimester { get; set; }
}

public class RecentRubricItem
{
    public string CourseCode { get; set; } = "";
    public string AssessmentName { get; set; } = "";
    public DateTime CreatedDate { get; set; }
}