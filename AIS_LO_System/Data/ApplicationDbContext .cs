using Microsoft.EntityFrameworkCore;
using AIS_LO_System.Models;
using System.Collections.Generic;

namespace AIS_LO_System.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        //Adding Student DbSet to ApplicationDbContext
        public DbSet<Student> Students { get; set; }

        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<AssignmentFile> AssignmentFiles { get; set; }
        public DbSet<Rubric> Rubrics { get; set; }
        public DbSet<RubricCriterion> RubricCriteria { get; set; }
        public DbSet<RubricLevel> RubricLevels { get; set; }

        //database set for learning outcomes and their mappings to criteria
        public DbSet<LearningOutcome> LearningOutcomes { get; set; }
        public DbSet<CriterionLOMapping> CriterionLOMappings { get; set; }

        public DbSet<StudentAssessmentMark> StudentAssessmentMarks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<CriterionLOMapping>()
            .Property(c => c.Weight)
            .HasPrecision(18, 2);


            modelBuilder.Entity<Student>().HasData(
                new Student { Id = 1, StudentId = "2026001", FullName = "Aarav Sharma" },
                new Student { Id = 2, StudentId = "2026002", FullName = "Priya Patel" },
                new Student { Id = 3, StudentId = "2026003", FullName = "John Smith" },
                new Student { Id = 4, StudentId = "2026004", FullName = "Meera Nair" },
                new Student { Id = 5, StudentId = "2026005", FullName = "Ali Khan" }
            );

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
            
            modelBuilder.Entity<StudentAssessmentMark>().HasData(
                new StudentAssessmentMark
                {
                    Id = 1,
                    StudentRefId = 2,
                    CourseCode = "COMP720",
                    AssessmentName = "Assignment 1",
                    IsMarked = true
                },
                new StudentAssessmentMark
                {
                    Id = 2,
                    StudentRefId = 4,
                    CourseCode = "COMP720",
                    AssessmentName = "Assignment 1",
                    IsMarked = true
                }
);

        } 

    }
}
