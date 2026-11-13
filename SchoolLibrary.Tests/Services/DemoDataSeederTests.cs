using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Data.Seeding;

namespace SchoolLibrary.Tests.Services;

public class DemoDataSeederTests : IDisposable
{
    // Meets the default Identity rules: a digit, lower and upper case letters, a symbol
    private const string Password = "Demo-Pass1!x";

    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;

    public DemoDataSeederTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    private Task<DemoSeedResult> SeedAsync(string password = Password) =>
        DemoDataSeeder.SeedAsync(_scope.ServiceProvider, password);

    // Resources that exist before the demo seed, like the imported free resources
    private async Task AddExistingResourcesAsync(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            _db.Resources.Add(new Resource
            {
                Title = "Ресурс " + i,
                Author = "Автор",
                Url = "https://example.com/" + i,
                NormalizedUrl = "https://example.com/" + i,
                Kind = ResourceKind.External,
                CreatedOn = DateTime.UtcNow.AddDays(-i)
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task<ApplicationUser> UserAsync(string role)
    {
        // The e-mail is worked out first: the array lookup cannot be part of the database query
        var email = DemoDataSeeder.Accounts.Single(a => a.Role == role).Email;
        return await _db.Users.SingleAsync(u => u.Email == email);
    }

    // ---------- accounts ----------

    [Fact]
    public async Task SeedAsync_CreatesOneAccountPerRole()
    {
        var result = await SeedAsync();

        Assert.Equal(4, result.Users);
        Assert.Equal(4, _db.Users.Count());
        var userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var account in DemoDataSeeder.Accounts)
        {
            var user = await userManager.FindByEmailAsync(account.Email);
            Assert.NotNull(user);
            Assert.True(await userManager.IsInRoleAsync(user!, account.Role));
            Assert.True(await userManager.IsInRoleAsync(user!, Roles.User));
            Assert.Equal(account.FirstName, user!.FirstName);
        }
    }

    [Fact]
    public async Task SeedAsync_AccountsCanSignInWithThePassword()
    {
        await SeedAsync();
        var userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var teacher = await UserAsync(Roles.Teacher);

        Assert.True(await userManager.CheckPasswordAsync(teacher, Password));
        Assert.False(await userManager.CheckPasswordAsync(teacher, "wrong"));
    }

    [Fact]
    public async Task SeedAsync_WeakPassword_FailsAndNamesTheAccount()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAsync("abc"));

        Assert.Contains("admin@demo.local", error.Message);
    }

    [Fact]
    public async Task SeedAsync_OnlyAddsTheRolesThatAreMissing()
    {
        var roleManager = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await roleManager.CreateAsync(new IdentityRole(Roles.Teacher));

        await SeedAsync();

        Assert.Equal(Roles.All.Length, _db.Roles.Count());
    }

    // ---------- lessons ----------

    [Fact]
    public async Task SeedAsync_AddsLessonsOwnedByTheDemoTeacher()
    {
        _db.Licenses.Add(new License { Name = "CC BY-NC 4.0" });
        _db.Categories.Add(new Category { Name = "Математика" });
        _db.GradeLevels.Add(new GradeLevel { Number = 5 });
        await _db.SaveChangesAsync();

        var result = await SeedAsync();

        Assert.Equal(3, result.Lessons);
        var teacher = await UserAsync(Roles.Teacher);
        var lessons = await _db.Resources.Include(r => r.License).Include(r => r.Categories).Include(r => r.GradeLevels)
            .Where(r => r.OwnerId == teacher.Id).ToListAsync();
        Assert.Equal(3, lessons.Count);
        Assert.All(lessons, l =>
        {
            Assert.Equal(ResourceKind.Lesson, l.Kind);
            Assert.Equal("CC BY-NC 4.0", l.License!.Name);
            Assert.Single(l.Categories);
            Assert.Single(l.GradeLevels);
            Assert.False(string.IsNullOrEmpty(l.NormalizedUrl));
            Assert.True(l.IsEmbeddable);
        });
    }

    [Fact]
    public async Task SeedAsync_WithoutReferenceData_StillAddsTheLessons()
    {
        var result = await SeedAsync();

        Assert.Equal(3, result.Lessons);
        Assert.All(_db.Resources, r => Assert.Null(r.LicenseId));
    }

    [Fact]
    public async Task SeedAsync_SkipsALessonWhoseLinkIsAlreadyInTheLibrary()
    {
        _db.Resources.Add(new Resource
        {
            Title = "Вече има",
            Author = "А",
            Url = "http://www.phet.colorado.edu/sims/html/fractions-intro/latest/fractions-intro_all.html?utm_source=x",
            NormalizedUrl = "https://phet.colorado.edu/sims/html/fractions-intro/latest/fractions-intro_all.html",
            CreatedOn = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await SeedAsync();

        Assert.Equal(2, result.Lessons);
    }

    // ---------- comments, favorites, reports ----------

    [Fact]
    public async Task SeedAsync_AddsTenCommentsFromDifferentAuthorsOnFewResources()
    {
        await AddExistingResourcesAsync(6);

        var result = await SeedAsync();

        Assert.Equal(10, result.Comments);
        var comments = await _db.Comments.ToListAsync();
        Assert.Equal(10, comments.Count);
        Assert.Equal(4, comments.Select(c => c.AuthorId).Distinct().Count());
        Assert.True(comments.Select(c => c.ResourceId).Distinct().Count() <= 5);
        Assert.All(comments, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Text));
            Assert.True(c.Text.Length <= 500);
            Assert.True(c.CreatedOn < DateTime.UtcNow);
        });
    }

    [Fact]
    public async Task SeedAsync_AddsFavoritesForStudentParentAndTeacher()
    {
        await AddExistingResourcesAsync(6);

        var result = await SeedAsync();

        Assert.Equal(9, result.Favorites);
        var student = await UserAsync(Roles.Student);
        var parent = await UserAsync(Roles.Parent);
        var teacher = await UserAsync(Roles.Teacher);
        Assert.Equal(4, _db.Favorites.Count(f => f.UserId == student.Id));
        Assert.Equal(2, _db.Favorites.Count(f => f.UserId == parent.Id));
        Assert.Equal(3, _db.Favorites.Count(f => f.UserId == teacher.Id));
    }

    [Fact]
    public async Task SeedAsync_AddsTwoNewReportsAndOneReviewed_IncludingACommentReport()
    {
        await AddExistingResourcesAsync(6);

        var result = await SeedAsync();

        Assert.Equal(3, result.Reports);
        var reports = await _db.Reports.ToListAsync();
        Assert.Equal(2, reports.Count(r => r.Status == ReportStatus.New));
        var reviewed = Assert.Single(reports, r => r.Status == ReportStatus.Reviewed);
        Assert.NotNull(reviewed.ReviewedOn);

        var commentReport = Assert.Single(reports, r => r.Target == ReportTarget.Comment);
        Assert.NotNull(commentReport.CommentId);
        Assert.False(string.IsNullOrEmpty(commentReport.TargetText));
        Assert.All(reports, r => Assert.False(string.IsNullOrEmpty(r.TargetTitle)));
    }

    // ---------- running twice ----------

    [Fact]
    public async Task SeedAsync_RunTwice_AddsNothingTheSecondTime()
    {
        await AddExistingResourcesAsync(6);
        await SeedAsync();
        var counts = (_db.Users.Count(), _db.Resources.Count(), _db.Comments.Count(), _db.Favorites.Count(), _db.Reports.Count());

        var second = await SeedAsync();

        Assert.Equal(new DemoSeedResult(0, 0, 0, 0, 0), second);
        Assert.Equal(counts, (_db.Users.Count(), _db.Resources.Count(), _db.Comments.Count(), _db.Favorites.Count(), _db.Reports.Count()));
    }

    [Fact]
    public async Task SeedAsync_DoesNotTouchTheExistingResources()
    {
        await AddExistingResourcesAsync(3);

        await SeedAsync();

        var existing = await _db.Resources.Where(r => r.Title.StartsWith("Ресурс ")).ToListAsync();
        Assert.Equal(3, existing.Count);
        Assert.All(existing, r => Assert.Null(r.OwnerId));
    }
}
