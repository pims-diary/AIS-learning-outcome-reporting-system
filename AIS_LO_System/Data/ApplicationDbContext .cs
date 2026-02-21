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

        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<AssignmentFile> AssignmentFiles { get; set; }
        public DbSet<Rubric> Rubrics { get; set; }
        public DbSet<RubricCriterion> RubricCriteria { get; set; }
        public DbSet<RubricLevel> RubricLevels { get; set; }

        //database set for learning outcomes and their mappings to criteria
        public DbSet<LearningOutcome> LearningOutcomes { get; set; }
        public DbSet<CriterionLOMapping> CriterionLOMappings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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
        }

    }
}
