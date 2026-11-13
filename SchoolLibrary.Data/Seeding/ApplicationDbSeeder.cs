using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        ("Свободен за образователна употреба", "Публикуван от институция за свободно образователно ползване.", null)
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        await SeedGradeLevelsAsync(db);
        await SeedCategoriesAsync(db);
        await SeedLicensesAsync(db);

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
