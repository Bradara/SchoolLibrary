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

            // One row per user and resource; removing either removes the favorites that point to it
            builder.Entity<Favorite>()
                .HasKey(f => new { f.UserId, f.ResourceId });

            builder.Entity<Favorite>()
                .HasOne(f => f.User)
                .WithMany()
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Favorite>()
                .HasOne(f => f.Resource)
                .WithMany()
                .HasForeignKey(f => f.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Comments go away with their resource and with their author's account
            builder.Entity<Comment>()
                .HasOne(c => c.Resource)
                .WithMany()
                .HasForeignKey(c => c.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Comment>()
                .HasOne(c => c.Author)
                .WithMany()
                .HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);

            // The comments of one resource are always read newest first
            builder.Entity<Comment>()
                .HasIndex(c => new { c.ResourceId, c.CreatedOn });

            // One resource per link. PostgreSQL allows several NULLs in a unique index,
            // so resources that were not backfilled yet do not clash
            builder.Entity<Resource>()
                .HasIndex(r => r.NormalizedUrl)
                .IsUnique();

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
        public DbSet<Favorite> Favorites { get; set; }
        public DbSet<Comment> Comments { get; set; }
    }
}
