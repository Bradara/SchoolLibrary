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

            // A report outlives what it reports and who sent it: the links become NULL, the texts stay
            builder.Entity<Report>()
                .HasOne(r => r.Resource)
                .WithMany()
                .HasForeignKey(r => r.ResourceId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Report>()
                .HasOne(r => r.Comment)
                .WithMany()
                .HasForeignKey(r => r.CommentId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Report>()
                .HasOne(r => r.Reporter)
                .WithMany()
                .HasForeignKey(r => r.ReporterId)
                .OnDelete(DeleteBehavior.SetNull);

            // The moderation list: filtered by status, newest first
            builder.Entity<Report>()
                .HasIndex(r => new { r.Status, r.CreatedOn });

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

            builder.Entity<OnlineLibrary>()
                .HasIndex(l => l.Name)
                .IsUnique();

            // An uploaded resource has exactly one file; deleting the resource deletes the file row.
            // The stored bytes are removed separately by the service.
            builder.Entity<ResourceFile>()
                .HasOne(f => f.Resource)
                .WithOne(r => r.File)
                .HasForeignKey<ResourceFile>(f => f.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            // One row per stored key; the hash index finds a file already uploaded
            builder.Entity<ResourceFile>()
                .HasIndex(f => f.StorageKey)
                .IsUnique();

            builder.Entity<ResourceFile>()
                .HasIndex(f => f.Sha256);

            // ---------- Quizzes ----------

            // Deleting a teacher keeps their quizzes (OwnerId becomes NULL, like resources)
            builder.Entity<Quiz>()
                .HasOne(q => q.Owner)
                .WithMany()
                .HasForeignKey(q => q.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);

            // A quiz may point at a lesson; deleting the lesson keeps the quiz (link becomes NULL)
            builder.Entity<Quiz>()
                .HasOne(q => q.Resource)
                .WithMany()
                .HasForeignKey(q => q.ResourceId)
                .OnDelete(DeleteBehavior.SetNull);

            // One exam per code. PostgreSQL allows several NULLs in a unique index, so practice
            // quizzes (Code = NULL) do not clash
            builder.Entity<Quiz>()
                .HasIndex(q => q.Code)
                .IsUnique();

            // Listing: practice vs exam, published first
            builder.Entity<Quiz>()
                .HasIndex(q => new { q.Mode, q.IsPublished });

            // A quiz owns its questions, a question owns its options: deleting the parent removes them
            builder.Entity<Question>()
                .HasOne(q => q.Quiz)
                .WithMany(z => z.Questions)
                .HasForeignKey(q => q.QuizId)
                .OnDelete(DeleteBehavior.Cascade);

            // A question is filed in a section (bank topic); a section cannot be deleted while it holds
            // questions (enforced in the service and by Restrict here)
            builder.Entity<Question>()
                .HasOne(q => q.Section)
                .WithMany(s => s.Questions)
                .HasForeignKey(q => q.SectionId)
                .OnDelete(DeleteBehavior.Restrict);

            // Deleting a teacher keeps their sections (OwnerId becomes NULL, like quizzes)
            builder.Entity<Section>()
                .HasOne(s => s.Owner)
                .WithMany()
                .HasForeignKey(s => s.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);

            // Sections form a tree; a section cannot be deleted while it has subsections (enforced in
            // the service and by Restrict here)
            builder.Entity<Section>()
                .HasOne(s => s.Parent)
                .WithMany(s => s.Children)
                .HasForeignKey(s => s.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AnswerOption>()
                .HasOne(o => o.Question)
                .WithMany(q => q.Options)
                .HasForeignKey(o => o.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            // A percentage from -100 to 100 with two decimals (Moodle uses values such as 33.33333)
            builder.Entity<AnswerOption>()
                .Property(o => o.Fraction)
                .HasPrecision(5, 2);

            // Room for both large values and small tolerances such as 0.000001
            builder.Entity<AnswerOption>()
                .Property(o => o.Tolerance)
                .HasPrecision(18, 6);

            // The background checker looks for the code answers that still wait to be run
            builder.Entity<AttemptAnswer>()
                .HasIndex(a => a.CodeCheckPending);

            // The checks belong to the attempt; a checked question cannot be deleted (like an answered one)
            builder.Entity<CodeCheck>()
                .HasOne(c => c.Attempt)
                .WithMany(a => a.CodeChecks)
                .HasForeignKey(c => c.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<CodeCheck>()
                .HasOne(c => c.Question)
                .WithMany()
                .HasForeignKey(c => c.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<CodeCheck>()
                .HasIndex(c => new { c.AttemptId, c.QuestionId });

            // A code question owns its test cases
            builder.Entity<CodeTestCase>()
                .HasOne(t => t.Question)
                .WithMany(q => q.TestCases)
                .HasForeignKey(t => t.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<CodeTestCase>()
                .Property(t => t.Weight)
                .HasPrecision(7, 2);

            // A quiz owns its composition items; a referenced bank question or section is kept (Restrict)
            builder.Entity<QuizItem>()
                .HasOne(i => i.Quiz)
                .WithMany(q => q.Items)
                .HasForeignKey(i => i.QuizId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<QuizItem>()
                .HasOne(i => i.Question)
                .WithMany()
                .HasForeignKey(i => i.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<QuizItem>()
                .HasOne(i => i.Section)
                .WithMany()
                .HasForeignKey(i => i.SectionId)
                .OnDelete(DeleteBehavior.Restrict);

            // Deleting a quiz removes its attempts; an attempt removes its answers
            builder.Entity<QuizAttempt>()
                .HasOne(a => a.Quiz)
                .WithMany()
                .HasForeignKey(a => a.QuizId)
                .OnDelete(DeleteBehavior.Cascade);

            // An attempt owns its question snapshot; the referenced question is kept (Restrict)
            builder.Entity<AttemptQuestion>()
                .HasOne(aq => aq.Attempt)
                .WithMany(a => a.Questions)
                .HasForeignKey(aq => aq.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<AttemptQuestion>()
                .HasOne(aq => aq.Question)
                .WithMany()
                .HasForeignKey(aq => aq.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AttemptAnswer>()
                .HasOne(a => a.Attempt)
                .WithMany(t => t.Answers)
                .HasForeignKey(a => a.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);

            // An answer points at a question and the chosen option without cascading from them, so
            // these are the only paths that delete answer rows (avoids multiple cascade paths)
            builder.Entity<AttemptAnswer>()
                .HasOne(a => a.Question)
                .WithMany()
                .HasForeignKey(a => a.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AttemptAnswer>()
                .HasOne(a => a.SelectedOption)
                .WithMany()
                .HasForeignKey(a => a.SelectedOptionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AttemptAnswer>()
                .Property(a => a.AwardedPoints)
                .HasPrecision(7, 2);

            // The parts of an answer go with it; the options they refer to are kept (Restrict),
            // like SelectedOption above
            builder.Entity<AttemptAnswerChoice>()
                .HasOne(c => c.AttemptAnswer)
                .WithMany(a => a.Choices)
                .HasForeignKey(c => c.AttemptAnswerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<AttemptAnswerChoice>()
                .HasOne(c => c.Option)
                .WithMany()
                .HasForeignKey(c => c.OptionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AttemptAnswerChoice>()
                .HasOne(c => c.MatchedOption)
                .WithMany()
                .HasForeignKey(c => c.MatchedOptionId)
                .OnDelete(DeleteBehavior.Restrict);

            // An option appears at most once in an answer
            builder.Entity<AttemptAnswerChoice>()
                .HasIndex(c => new { c.AttemptAnswerId, c.OptionId })
                .IsUnique();

            // Points can be fractional (partial credit); two decimals are enough for display and grading
            builder.Entity<QuizAttempt>()
                .Property(a => a.Score)
                .HasPrecision(7, 2);

            builder.Entity<QuizAttempt>()
                .Property(a => a.MaxScore)
                .HasPrecision(7, 2);

            // Teacher review: the attempts of one quiz, newest submission first
            builder.Entity<QuizAttempt>()
                .HasIndex(a => new { a.QuizId, a.SubmittedOn });

            // An attempt is addressed by its token in the exam URLs
            builder.Entity<QuizAttempt>()
                .HasIndex(a => a.Token)
                .IsUnique();

            // ---------- Classes and class exams ----------

            // Deleting a teacher keeps their classes (OwnerId becomes NULL, like quizzes)
            builder.Entity<SchoolClass>()
                .HasOne(c => c.Owner)
                .WithMany()
                .HasForeignKey(c => c.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);

            // A teacher cannot have two classes with the same name
            builder.Entity<SchoolClass>()
                .HasIndex(c => new { c.OwnerId, c.Name })
                .IsUnique();

            // Deleting a class or the quiz removes its exams; an exam removes its attempts
            builder.Entity<ExamAssignment>()
                .HasOne(e => e.SchoolClass)
                .WithMany(c => c.Exams)
                .HasForeignKey(e => e.SchoolClassId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ExamAssignment>()
                .HasOne(e => e.Quiz)
                .WithMany()
                .HasForeignKey(e => e.QuizId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ExamAssignment>()
                .HasIndex(e => e.Code)
                .IsUnique();

            builder.Entity<QuizAttempt>()
                .HasOne(a => a.ExamAssignment)
                .WithMany(e => e.Attempts)
                .HasForeignKey(a => a.ExamAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);

            // One attempt per number in a class exam. Quiz-mode attempts have ExamAssignmentId = NULL,
            // and PostgreSQL lets NULLs repeat in a unique index, so they do not clash
            builder.Entity<QuizAttempt>()
                .HasIndex(a => new { a.ExamAssignmentId, a.StudentNumber })
                .IsUnique();
        }

        public DbSet<Resource> Resources { get; set; }
        public DbSet<ResourceFile> ResourceFiles { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<GradeLevel> GradeLevels { get; set; }
        public DbSet<License> Licenses { get; set; }
        public DbSet<OnlineLibrary> OnlineLibraries { get; set; }
        public DbSet<Favorite> Favorites { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<Section> Sections { get; set; }
        public DbSet<Quiz> Quizzes { get; set; }
        public DbSet<SchoolClass> SchoolClasses { get; set; }
        public DbSet<ExamAssignment> ExamAssignments { get; set; }
        public DbSet<QuizItem> QuizItems { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<AnswerOption> AnswerOptions { get; set; }
        public DbSet<CodeTestCase> CodeTestCases { get; set; }
        public DbSet<CodeCheck> CodeChecks { get; set; }
        public DbSet<QuizAttempt> QuizAttempts { get; set; }
        public DbSet<AttemptQuestion> AttemptQuestions { get; set; }
        public DbSet<AttemptAnswer> AttemptAnswers { get; set; }
        public DbSet<AttemptAnswerChoice> AttemptAnswerChoices { get; set; }
    }
}
