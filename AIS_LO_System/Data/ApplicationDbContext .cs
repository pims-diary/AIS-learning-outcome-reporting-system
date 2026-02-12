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
    }
}
