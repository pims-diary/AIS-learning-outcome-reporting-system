using Microsoft.EntityFrameworkCore;
using AIS_LO_System.Models;

namespace AIS_LO_System.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Admin / Auth
        public DbSet<AppUser> AppUsers { get; set; }

        // Courses
        public DbSet<Course> Courses { get; set; }
        public DbSet<LecturerCourseEnrolment> LecturerCourseEnrolments { get; set; }

        // Students
        public DbSet<Student> Students { get; set; }
        public DbSet<StudentCourseEnrolment> StudentCourseEnrolments { get; set; }

        // Assignments
        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<AssignmentFile> AssignmentFiles { get; set; }
        public DbSet<Rubric> Rubrics { get; set; }
        public DbSet<RubricCriterion> RubricCriteria { get; set; }
        public DbSet<RubricLevel> RubricLevels { get; set; }

        // Learning Outcomes
        public DbSet<LearningOutcome> LearningOutcomes { get; set; }
        public DbSet<CriterionLOMapping> CriterionLOMappings { get; set; }

        // Marking
        public DbSet<StudentAssessmentMark> StudentAssessmentMarks { get; set; }
        public DbSet<StudentCriterionMark> StudentCriterionMarks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Course → Lecturer (optional FK, NoAction to avoid multiple cascade paths on SQL Server)
            modelBuilder.Entity<Course>()
                .HasOne(c => c.Lecturer)
                .WithMany()
                .HasForeignKey(c => c.LecturerId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            // Course → Moderator (optional FK, NoAction same reason)
            modelBuilder.Entity<Course>()
                .HasOne(c => c.Moderator)
                .WithMany()
                .HasForeignKey(c => c.ModeratorId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            // Unique: one AppUser per Course per enrolment
            modelBuilder.Entity<LecturerCourseEnrolment>()
                .HasIndex(e => new { e.UserId, e.CourseId })
                .IsUnique();

            // Unique: one Student per Course per enrolment
            modelBuilder.Entity<StudentCourseEnrolment>()
                .HasIndex(e => new { e.StudentId, e.CourseId })
                .IsUnique();

            // Unique: AppUser username must be unique
            modelBuilder.Entity<AppUser>()
                .HasIndex(u => u.Username)
                .IsUnique();

            // CriterionLOMapping weight precision
            modelBuilder.Entity<CriterionLOMapping>()
                .Property(c => c.Weight)
                .HasPrecision(18, 2);

            // StudentCriterionMark precision (from Priyanka's branch)
            modelBuilder.Entity<StudentCriterionMark>()
                .Property(x => x.Weight)
                .HasPrecision(18, 2);

            modelBuilder.Entity<StudentCriterionMark>()
                .Property(x => x.CalculatedScore)
                .HasPrecision(18, 2);

            // Rubric cascade
            modelBuilder.Entity<Rubric>()
                .HasMany(r => r.Criteria)
                .WithOne(c => c.Rubric)
                .HasForeignKey(c => c.RubricId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RubricCriterion>()
                .HasMany(c => c.Levels)
                .WithOne(l => l.RubricCriterion)
                .HasForeignKey(l => l.RubricCriterionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Seed: default admin user — password is "Admin@123"
            modelBuilder.Entity<AppUser>().HasData(
                new AppUser
                {
                    Id = 1,
                    FullName = "Administrator",
                    Username = "admin",
                    PasswordHash = "$2a$11$8KzaNdKIMyOkASKQHxSO6.bKHQAiqTpnbNYUSGBuKZRtbOWTfDXSS",
                    Role = UserRole.Admin
                }
            );

            // Seed: courses
            modelBuilder.Entity<Course>().HasData(
                new Course { Id = 1, Code = "INFO712", Title = "Management Information Systems", Year = 2026, Trimester = 1, School = "Information Technology", CanEditLO = true, CanReuploadOutline = true },
                new Course { Id = 2, Code = "SOFT703", Title = "Web Applications Development", Year = 2026, Trimester = 1, School = "Information Technology", CanEditLO = true, CanReuploadOutline = true },
                new Course { Id = 3, Code = "COMP720", Title = "Information Technology Project", Year = 2026, Trimester = 1, School = "Information Technology", CanEditLO = true, CanReuploadOutline = true },
                new Course { Id = 4, Code = "COMP701", Title = "Software Engineering", Year = 2026, Trimester = 2, School = "Information Technology", CanEditLO = true, CanReuploadOutline = true },
                new Course { Id = 5, Code = "COMP703", Title = "Web App Dev (ASP.NET)", Year = 2026, Trimester = 2, School = "Information Technology", CanEditLO = true, CanReuploadOutline = true },
                new Course { Id = 6, Code = "COMP610", Title = "Database Systems", Year = 2025, Trimester = 1, School = "Information Technology", CanEditLO = true, CanReuploadOutline = true },
                new Course { Id = 7, Code = "COMP611", Title = "Systems Analysis", Year = 2025, Trimester = 1, School = "Information Technology", CanEditLO = true, CanReuploadOutline = true },
                new Course { Id = 8, Code = "INFO600", Title = "Intro to IT", Year = 2024, Trimester = 2, School = "Information Technology", CanEditLO = true, CanReuploadOutline = true }
            );

            // Seed: students
            modelBuilder.Entity<Student>().HasData(
                new Student { Id = 1, StudentId = "2026001", FullName = "Aarav Sharma" },
                new Student { Id = 2, StudentId = "2026002", FullName = "Priya Patel" },
                new Student { Id = 3, StudentId = "2026003", FullName = "John Smith" },
                new Student { Id = 4, StudentId = "2026004", FullName = "Meera Nair" },
                new Student { Id = 5, StudentId = "2026005", FullName = "Ali Khan" }
            );

            // Seed: assessment marks
            modelBuilder.Entity<StudentAssessmentMark>().HasData(
                new StudentAssessmentMark { Id = 1, StudentRefId = 2, CourseCode = "COMP720", AssessmentName = "Assignment 1", IsMarked = true },
                new StudentAssessmentMark { Id = 2, StudentRefId = 4, CourseCode = "COMP720", AssessmentName = "Assignment 1", IsMarked = true }
            );
        }
    }
}