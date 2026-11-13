using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Data.Seeding;

/// <summary>What one run of the demo seed added.</summary>
public record DemoSeedResult(int Users, int Lessons, int Comments, int Favorites, int Reports);

/// <summary>
/// Demo data for development and for showing the application: one account per role, a few lessons by the
/// demo teacher, and comments, favorites and reports on the resources that exist. Every part is skipped when
/// it is already there, so it is safe to run on each start. The caller decides when it runs (see Program.cs):
/// only in Development and only when the setting is on; the password comes from the configuration.
/// </summary>
public static class DemoDataSeeder
{
    public const string EmailDomain = "demo.local";

    /// <summary>The demo accounts: e-mail (also the user name), first name, last name, role.</summary>
    public static readonly (string Email, string FirstName, string LastName, string Role)[] Accounts =
    {
        ($"admin@{EmailDomain}", "Демо", "Администратор", Roles.Administrator),
        ($"teacher@{EmailDomain}", "Демо", "Учител", Roles.Teacher),
        ($"student@{EmailDomain}", "Демо", "Ученик", Roles.Student),
        ($"parent@{EmailDomain}", "Демо", "Родител", Roles.Parent)
    };

    private static readonly string[] CommentTexts =
    {
        "Много полезна симулация, учениците ми я харесаха!",
        "Може ли да се използва и като домашна работа?",
        "Благодаря за споделянето. Ще го ползвам в клас.",
        "Дъщеря ми го разгледа вечерта и разбра темата по-добре.",
        "Хубав материал, но линкът понякога се зарежда бавно.",
        "Добавих го в любими, за да го намеря бързо.",
        "Има ли подобен материал и за по-големите класове?",
        "Много нагледно. Препоръчвам го на колегите.",
        "Лицензът е ясен, така че мога спокойно да го показвам на уроците.",
        "Благодаря! Точно това търсех за темата."
    };

    // Lessons the demo teacher "shared": interactive simulations that are embeddable (title, address, description)
    private static readonly (string Title, string Url, string Description)[] Lessons =
    {
        ("Демо урок: Дроби (интерактивна симулация)", "https://phet.colorado.edu/sims/html/fractions-intro/latest/fractions-intro_all.html",
            "Демо данни: урок, в който учителят споделя симулация за дроби."),
        ("Демо урок: Плътност (интерактивна симулация)", "https://phet.colorado.edu/sims/html/density/latest/density_all.html",
            "Демо данни: урок, в който учителят споделя симулация за плътността на веществата."),
        ("Демо урок: Сили и движение (интерактивна симулация)", "https://phet.colorado.edu/sims/html/forces-and-motion-basics/latest/forces-and-motion-basics_all.html",
            "Демо данни: урок, в който учителят споделя симулация за силите и движението.")
    };

    public static async Task<DemoSeedResult> SeedAsync(IServiceProvider services, string password)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        await EnsureRolesAsync(roleManager);
        var (users, createdUsers) = await EnsureUsersAsync(userManager, password);

        var teacher = users[Roles.Teacher];
        var lessons = await AddLessonsAsync(db, teacher);
        var resourceIds = await db.Resources.OrderBy(r => r.Id).Select(r => r.Id).ToListAsync();

        var commentUsers = Accounts.Select(a => users[a.Role]).ToList();
        var comments = await AddCommentsAsync(db, commentUsers, resourceIds);
        var favorites = await AddFavoritesAsync(db, users, resourceIds);
        var reports = await AddReportsAsync(db, users, resourceIds);

        return new DemoSeedResult(createdUsers, lessons, comments, favorites, reports);
    }

    private static async Task EnsureRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task<(Dictionary<string, ApplicationUser> ByRole, int Created)> EnsureUsersAsync(
        UserManager<ApplicationUser> userManager, string password)
    {
        var byRole = new Dictionary<string, ApplicationUser>();
        var created = 0;

        foreach (var (email, firstName, lastName, role) in Accounts)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    FirstName = firstName,
                    LastName = lastName,
                    CreatedOn = DateTime.UtcNow
                };

                var result = await userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                {
                    // Most often the password does not meet the Identity password rules
                    throw new InvalidOperationException(
                        "The demo account " + email + " could not be created: "
                        + string.Join(" ", result.Errors.Select(e => e.Description)));
                }

                created++;
            }

            foreach (var roleName in new[] { Roles.User, role })
            {
                if (!await userManager.IsInRoleAsync(user, roleName))
                {
                    await userManager.AddToRoleAsync(user, roleName);
                }
            }

            byRole[role] = user;
        }

        return (byRole, created);
    }

    private static async Task<int> AddLessonsAsync(ApplicationDbContext db, ApplicationUser teacher)
    {
        if (await db.Resources.AnyAsync(r => r.OwnerId == teacher.Id))
        {
            return 0;
        }

        var category = await db.Categories.OrderBy(c => c.Id).FirstOrDefaultAsync();
        var grade = await db.GradeLevels.OrderBy(g => g.Number).FirstOrDefaultAsync();
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Name == "CC BY-NC 4.0");
        var added = 0;

        foreach (var (title, url, description) in Lessons)
        {
            var normalized = Common.UrlNormalizer.Normalize(url);
            if (await db.Resources.AnyAsync(r => r.NormalizedUrl == normalized))
            {
                continue;
            }

            var resource = new Resource
            {
                Title = title,
                Author = "PhET Interactive Simulations, University of Colorado Boulder",
                Source = "PhET (phet.colorado.edu)",
                Description = description,
                Url = url,
                NormalizedUrl = normalized,
                Kind = ResourceKind.Lesson,
                OwnerId = teacher.Id,
                License = license,
                CreatedOn = DateTime.UtcNow
            };

            if (category != null) resource.Categories.Add(category);
            if (grade != null) resource.GradeLevels.Add(grade);

            db.Resources.Add(resource);
            added++;
        }

        await db.SaveChangesAsync();
        return added;
    }

    private static async Task<int> AddCommentsAsync(ApplicationDbContext db, List<ApplicationUser> authors, List<int> resourceIds)
    {
        var authorIds = authors.Select(a => a.Id).ToList();
        if (resourceIds.Count == 0 || await db.Comments.AnyAsync(c => authorIds.Contains(c.AuthorId)))
        {
            return 0;
        }

        // The comments are spread over the first resources, oldest first, with different authors and times
        var targets = resourceIds.Take(5).ToList();
        for (var i = 0; i < CommentTexts.Length; i++)
        {
            db.Comments.Add(new Comment
            {
                ResourceId = targets[i % targets.Count],
                AuthorId = authorIds[i % authorIds.Count],
                Text = CommentTexts[i],
                CreatedOn = DateTime.UtcNow.AddHours(-(CommentTexts.Length - i) * 5)
            });
        }

        await db.SaveChangesAsync();
        return CommentTexts.Length;
    }

    private static async Task<int> AddFavoritesAsync(ApplicationDbContext db, Dictionary<string, ApplicationUser> users, List<int> resourceIds)
    {
        // How many of the first resources each role saves
        var plan = new[] { (Roles.Student, 4), (Roles.Parent, 2), (Roles.Teacher, 3) };
        var added = 0;

        foreach (var (role, count) in plan)
        {
            var userId = users[role].Id;
            foreach (var resourceId in resourceIds.Take(count))
            {
                if (await db.Favorites.AnyAsync(f => f.UserId == userId && f.ResourceId == resourceId))
                {
                    continue;
                }

                db.Favorites.Add(new Favorite { UserId = userId, ResourceId = resourceId, CreatedOn = DateTime.UtcNow });
                added++;
            }
        }

        await db.SaveChangesAsync();
        return added;
    }

    private static async Task<int> AddReportsAsync(ApplicationDbContext db, Dictionary<string, ApplicationUser> users, List<int> resourceIds)
    {
        var reporterIds = new[] { users[Roles.Student].Id, users[Roles.Parent].Id, users[Roles.Teacher].Id };
        if (resourceIds.Count < 2 || await db.Reports.AnyAsync(r => r.ReporterId != null && reporterIds.Contains(r.ReporterId)))
        {
            return 0;
        }

        var first = await db.Resources.AsNoTracking().FirstAsync(r => r.Id == resourceIds[0]);
        var second = await db.Resources.AsNoTracking().FirstAsync(r => r.Id == resourceIds[1]);
        var comment = await db.Comments.AsNoTracking().Include(c => c.Resource)
            .Where(c => c.AuthorId == users[Roles.Student].Id)
            .OrderBy(c => c.Id).FirstOrDefaultAsync();

        var reports = new List<Report>
        {
            // Two reports waiting for the administrator and one that was already handled
            new()
            {
                Target = ReportTarget.Resource, ResourceId = first.Id, TargetTitle = first.Title,
                Reason = ReportReason.Copyright, Details = "Мисля, че материалът е копиран без разрешение. Моля, проверете.",
                Status = ReportStatus.New, CreatedOn = DateTime.UtcNow.AddHours(-3), ReporterId = users[Roles.Student].Id
            },
            new()
            {
                Target = ReportTarget.Resource, ResourceId = second.Id, TargetTitle = second.Title,
                Reason = ReportReason.BrokenLink, Details = "Страницата не се зарежда.",
                Status = ReportStatus.Reviewed, ReviewedOn = DateTime.UtcNow.AddDays(-1),
                CreatedOn = DateTime.UtcNow.AddDays(-2), ReporterId = users[Roles.Teacher].Id
            }
        };

        if (comment != null)
        {
            reports.Add(new Report
            {
                Target = ReportTarget.Comment, ResourceId = comment.ResourceId, CommentId = comment.Id,
                TargetTitle = comment.Resource.Title, TargetText = comment.Text,
                Reason = ReportReason.Inappropriate, Details = null,
                Status = ReportStatus.New, CreatedOn = DateTime.UtcNow.AddHours(-1), ReporterId = users[Roles.Parent].Id
            });
        }

        db.Reports.AddRange(reports);
        await db.SaveChangesAsync();
        return reports.Count;
    }
}
