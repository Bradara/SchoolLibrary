using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolLibrary.Data.Common;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Data.Seeding;

/// <summary>
/// Applies migrations and seeds the reference data (grades, subjects, roles).
/// Every step is idempotent, so it is safe to run on each application start.
/// </summary>
public static class ApplicationDbSeeder
{
    private static readonly string[] CategoryNames =
    {
        "Български език и литература",
        "Английски език",
        "Немски език",
        "Френски език",
        "Руски език",
        "Испански език",
        "Италиански език",
        "Математика",
        "Информатика",
        "Информационни технологии",
        "Компютърно моделиране",
        "Компютърно моделиране и информационни технологии",
        "Роден край",
        "Околен свят",
        "Човекът и обществото",
        "Човекът и природата",
        "История и цивилизации",
        "География и икономика",
        "Философия",
        "Гражданско образование",
        "Биология и здравно образование",
        "Физика и астрономия",
        "Химия и опазване на околната среда",
        "Музика",
        "Изобразително изкуство",
        "Технологии и предприемачество",
        "Предприемачество",
        "Физическо възпитание и спорт",
        "Религия",
        "Хореография",
        "Друго"
    };

    private static readonly (string Name, string Description, string? Url)[] LicenseSeed =
    {
        ("Собствен материал", "Авторът споделя материала свободно в платформата.", null),
        ("С разрешение на автора", "Авторът е дал изрично разрешение за споделяне.", null),
        ("Обществено достояние", "Материалът не е защитен от авторско право.", null),
        ("CC0 1.0", "Авторът се отказва от всички права.", "https://creativecommons.org/publicdomain/zero/1.0/deed.bg"),
        ("CC BY 4.0", "Свободно ползване с посочване на автора.", "https://creativecommons.org/licenses/by/4.0/deed.bg"),
        ("CC BY-SA 4.0", "С посочване на автора и споделяне на производните работи при същите условия.", "https://creativecommons.org/licenses/by-sa/4.0/deed.bg"),
        ("CC BY-NC 4.0", "С посочване на автора, без търговска употреба.", "https://creativecommons.org/licenses/by-nc/4.0/deed.bg"),
        ("CC BY-NC-SA 4.0", "С посочване на автора, без търговска употреба, производните работи при същите условия.", "https://creativecommons.org/licenses/by-nc-sa/4.0/deed.bg"),
        ("CC BY-ND 4.0", "С посочване на автора, без промени.", "https://creativecommons.org/licenses/by-nd/4.0/deed.bg"),
        ("Свободен за образователна употреба", "Публикуван от институция за свободно образователно ползване.", null),
        ("CC BY 3.0", "Свободно ползване с посочване на автора (по-стара версия на лиценза).", "https://creativecommons.org/licenses/by/3.0/deed.bg"),
        ("MIT", "Свободен софтуерен лиценз: ползване и промяна със запазване на бележката за авторските права.", "https://opensource.org/license/mit"),
        ("Apache 2.0", "Свободен софтуерен лиценз: ползване и промяна със запазване на бележките за авторските права.", "https://www.apache.org/licenses/LICENSE-2.0"),
        ("Стандартен лиценз на YouTube", "Видеото се показва чрез плейъра на YouTube и остава собственост на автора; платформата не го копира.", "https://www.youtube.com/t/terms"),
        ("GPL 2.0", "Свободен софтуерен лиценз: програмата може да се ползва, променя и разпространява при същите условия.", "https://www.gnu.org/licenses/old-licenses/gpl-2.0.html"),
        ("CC BY-SA 2.0", "С посочване на автора и споделяне на производните работи при същите условия (по-стара версия на лиценза).", "https://creativecommons.org/licenses/by-sa/2.0/deed.bg"),
        ("Лиценз на GeoGebra", "Свободно ползване в училище и за обучение без търговска цел, според условията на GeoGebra.", "https://www.geogebra.org/license")
    };

    private static readonly (string Name, string Description, string Url, string Icon)[] OnlineLibrarySeed =
    {
        ("Читанка", "Свободна електронна библиотека с произведения на български и чужди автори, достъпни за четене онлайн и за изтегляне.", "https://chitanka.info", "bi-book"),
        ("Чети", "Онлайн библиотека с книги за четене, сред които и произведения от училищната програма.", "https://4eti.me", "bi-bookshelf"),
        ("Литернет", "Литературен портал с произведения, автори и материали по литература.", "https://liternet.bg", "bi-journal-text"),
        ("Словото", "Сайт за българска литература: автори, творби и справочни материали.", "https://slovo.bg", "bi-feather")
    };

    /// <summary>Names of the licenses that are seeded on every start.</summary>
    public static IReadOnlyList<string> StandardLicenseNames => LicenseSeed.Select(l => l.Name).ToList();

    /// <summary>Applies the migrations, then seeds the reference data and the roles.</summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        await SeedDataAsync(services);
    }

    /// <summary>
    /// Everything <see cref="SeedAsync"/> does except migrating. A separate method so that it can be
    /// run (and tested) on a database that cannot migrate, like the in-memory one.
    /// </summary>
    public static async Task SeedDataAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();

        await SeedGradeLevelsAsync(db);
        await SeedCategoriesAsync(db);
        await SeedLicensesAsync(db);
        await SeedOnlineLibrariesAsync(db);
        await BackfillNormalizedUrlsAsync(db, services.GetService<ILoggerFactory>()?.CreateLogger(nameof(ApplicationDbSeeder)));

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        await SeedRolesAsync(roleManager);
        await AssignRolesToExistingUsersAsync(userManager);
    }

    private static async Task SeedGradeLevelsAsync(ApplicationDbContext db)
    {
        if (await db.GradeLevels.AnyAsync())
        {
            return;
        }

        db.GradeLevels.AddRange(Enum.GetValues<Grades>().Select(g => new GradeLevel { Number = (int)g }));
        await db.SaveChangesAsync();
    }

    private static async Task SeedCategoriesAsync(ApplicationDbContext db)
    {
        if (await db.Categories.AnyAsync())
        {
            return;
        }

        db.Categories.AddRange(CategoryNames.Select(name => new Category { Name = name }));
        await db.SaveChangesAsync();
    }

    // Adds only the missing licenses, so the list can grow without touching existing ones
    private static async Task SeedLicensesAsync(ApplicationDbContext db)
    {
        var existing = await db.Licenses.Select(l => l.Name).ToListAsync();

        var missing = LicenseSeed
            .Where(l => !existing.Contains(l.Name))
            .Select(l => new License { Name = l.Name, Description = l.Description, Url = l.Url })
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        db.Licenses.AddRange(missing);
        await db.SaveChangesAsync();
    }

    // Only on an empty table: afterwards the administrator owns the list, and a deleted library must not come back
    private static async Task SeedOnlineLibrariesAsync(ApplicationDbContext db)
    {
        if (await db.OnlineLibraries.AnyAsync())
        {
            return;
        }

        db.OnlineLibraries.AddRange(OnlineLibrarySeed.Select((l, i) => new OnlineLibrary
        {
            Name = l.Name,
            Description = l.Description,
            Url = l.Url,
            Icon = l.Icon,
            SortOrder = (i + 1) * 10
        }));
        await db.SaveChangesAsync();
    }

    // Fills the normalized link of resources added before the column existed and reports shared links.
    // Uploaded resources have no link: their NormalizedUrl stays null (many nulls are allowed by the
    // unique index), so they are left out of the backfill and the duplicate report.
    private static async Task BackfillNormalizedUrlsAsync(ApplicationDbContext db, ILogger? logger)
    {
        var pending = await db.Resources
            .Where(r => r.NormalizedUrl == null && r.Kind != ResourceKind.Upload)
            .ToListAsync();
        if (pending.Count > 0)
        {
            foreach (var resource in pending)
            {
                // A link that cannot be normalized is kept as it was typed
                resource.NormalizedUrl = UrlNormalizer.Normalize(resource.Url) ?? resource.Url;
            }

            await db.SaveChangesAsync();
        }

        // Repair uploads an earlier version wrote an empty normalized link to (so many can coexist again)
        var mislabeledUploads = await db.Resources
            .Where(r => r.Kind == ResourceKind.Upload && r.NormalizedUrl != null)
            .ToListAsync();
        if (mislabeledUploads.Count > 0)
        {
            foreach (var upload in mislabeledUploads)
            {
                upload.NormalizedUrl = null;
            }

            await db.SaveChangesAsync();
        }

        var duplicates = await db.Resources
            .Where(r => r.NormalizedUrl != null)
            .GroupBy(r => r.NormalizedUrl)
            .Where(g => g.Count() > 1)
            .Select(g => new { Url = g.Key, Ids = g.Select(r => r.Id).ToList() })
            .ToListAsync();

        foreach (var duplicate in duplicates)
        {
            logger?.LogWarning("Resources {Ids} share the link {Url}", string.Join(", ", duplicate.Ids), duplicate.Url);
        }
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        // Existing databases have the role "Admin": rename it so users keep their role
        var legacyAdmin = await roleManager.FindByNameAsync(Roles.LegacyAdmin);
        if (legacyAdmin != null && !await roleManager.RoleExistsAsync(Roles.Administrator))
        {
            legacyAdmin.Name = Roles.Administrator;
            await roleManager.UpdateAsync(legacyAdmin);
        }

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task AssignRolesToExistingUsersAsync(UserManager<ApplicationUser> userManager)
    {
        var users = await userManager.Users.OrderBy(u => u.CreatedOn).ToListAsync();

        // Without any administrator the oldest user becomes one
        if (users.Count > 0 && (await userManager.GetUsersInRoleAsync(Roles.Administrator)).Count == 0)
        {
            await userManager.AddToRoleAsync(users[0], Roles.Administrator);
        }

        string[] specificRoles = { Roles.Administrator, Roles.Teacher, Roles.Student, Roles.Parent };

        foreach (var user in users)
        {
            if (!await userManager.IsInRoleAsync(user, Roles.User))
            {
                await userManager.AddToRoleAsync(user, Roles.User);
            }

            // Users created before the roles existed could add resources, so they become teachers
            var userRoles = await userManager.GetRolesAsync(user);
            if (!userRoles.Intersect(specificRoles).Any())
            {
                await userManager.AddToRoleAsync(user, Roles.Teacher);
            }
        }
    }
}
