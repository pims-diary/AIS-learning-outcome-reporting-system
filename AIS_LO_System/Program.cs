using QuestPDF.Infrastructure;
using AIS_LO_System.Data;
using AIS_LO_System.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation();
builder.Services.AddScoped<AIS_LO_System.Services.SubmissionService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

// -------------------------------------------------------
// Startup seeder: ensures the admin account has a valid
// BCrypt hash. Runs once on app start, safe to leave in.
// -------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        db.Database.Migrate();

        // One-time fix: add columns if migration recorded but columns missing
        try
        {
            db.Database.ExecuteSqlRaw(@"
                IF COL_LENGTH('Assignments','MarksPercentage') IS NULL
                BEGIN
                    ALTER TABLE [Assignments] ADD [MarksPercentage] int NOT NULL DEFAULT 0;
                    ALTER TABLE [Assignments] ADD [LOsLockedByOutline] bit NOT NULL DEFAULT 0;
                END
            ");
        }
        catch { }

        var admin = db.AppUsers.FirstOrDefault(u => u.Username == "admin");
        if (admin == null)
        {
            db.AppUsers.Add(new AppUser
            {
                FullName = "Administrator",
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                Role = UserRole.Admin
            });
            db.SaveChanges();
        }
        else if (!BCrypt.Net.BCrypt.Verify("Admin@123", admin.PasswordHash))
        {
            admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123");
            db.SaveChanges();
        }

        // Default lecturer account for development
        var lecturer = db.AppUsers.FirstOrDefault(u => u.Username == "lecturer");
        if (lecturer == null)
        {
            db.AppUsers.Add(new AppUser
            {
                FullName = "Default Lecturer",
                Username = "lecturer",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123"),
                Role = UserRole.Lecturer
            });
            db.SaveChanges();
        }
    }
    catch (Microsoft.Data.SqlClient.SqlException ex)
    {
        logger.LogError(ex, "Database migration or seeding failed (LocalDB connection/startup problem). The application will start, but database features may be unavailable.");
    }
}
// -------------------------------------------------------

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();