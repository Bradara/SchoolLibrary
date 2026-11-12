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
        }

        public DbSet<Resource> Resources { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<GradeLevel> GradeLevels { get; set; }
    }
}
