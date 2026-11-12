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

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        await SeedGradeLevelsAsync(db);
        await SeedCategoriesAsync(db);

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
