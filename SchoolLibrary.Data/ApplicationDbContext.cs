using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }



        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Deleting a user keeps their resources (OwnerId becomes NULL)
            builder.Entity<Resource>()
                .HasOne(r => r.Owner)
                .WithMany()
                .HasForeignKey(r => r.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);

            // A license in use cannot be deleted
            builder.Entity<Resource>()
                .HasOne(r => r.License)
                .WithMany()
                .HasForeignKey(r => r.LicenseId)
                .OnDelete(DeleteBehavior.Restrict);

            // Two licenses with the same name would be confusing in the drop-down
            builder.Entity<License>()
                .HasIndex(l => l.Name)
                .IsUnique();
        }

        public DbSet<Resource> Resources { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<GradeLevel> GradeLevels { get; set; }
        public DbSet<License> Licenses { get; set; }
    }
}
