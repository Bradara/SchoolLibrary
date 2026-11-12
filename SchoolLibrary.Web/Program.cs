using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' is not configured. " +
                    "Set it with user secrets (development) or the ConnectionStrings__DefaultConnection environment variable (Docker).");
            }

            // Keep the Identity cookie/antiforgery keys outside the container filesystem,
            // otherwise every container restart logs all users out.
            var keysPath = builder.Configuration["DataProtection:KeysPath"];
            if (!string.IsNullOrWhiteSpace(keysPath))
            {
                builder.Services.AddDataProtection()
                    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
                    .SetApplicationName("SchoolLibrary");
            }

            // The app runs behind a reverse proxy (nginx) that terminates TLS.
            // The proxy port is bound to 127.0.0.1 only, so trusting its headers is safe.
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            });

            // Enable retry on failure for PostgreSQL connection (database may start after the app in Docker)
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connectionString, npgsqlOptions =>
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 6,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorCodesToAdd: null)));

            builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            builder.Services
                .AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            builder.Services.AddControllersWithViews();

            var app = builder.Build();

            // Must run first so Request.Scheme / RemoteIpAddress reflect the original client request.
            app.UseForwardedHeaders();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            // In Production TLS is handled by the reverse proxy; the container only speaks HTTP.
            if (!app.Environment.IsProduction())
            {
                app.UseHttpsRedirection();
            }
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();
            app.UseStatusCodePagesWithReExecute("/Home/Error", "?code={0}");

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            // Seed на Admin ролята и първия admin
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Database.Migrate();

                // Seed grade levels (5-12) from the Grades enum when the table is empty
                if (!await db.GradeLevels.AnyAsync())
                {
                    db.GradeLevels.AddRange(
                        Enum.GetValues<Grades>().Select(g => new GradeLevel { Number = (int)g }));
                    await db.SaveChangesAsync();
                }

                // Seed default categories (school subjects) when the table is empty
                if (!await db.Categories.AnyAsync())
                {
                    string[] categoryNames =
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
                    db.Categories.AddRange(categoryNames.Select(n => new Category { Name = n }));
                    await db.SaveChangesAsync();
                }
                var services = scope.ServiceProvider;

                var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
                var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

                // Създай роля Админ, ако не съществува
                if (!await roleManager.RoleExistsAsync("Admin"))
                {
                    await roleManager.CreateAsync(new IdentityRole("Admin"));
                }

                var admins = await userManager.GetUsersInRoleAsync("Admin");
                //Проверява дали има Админ вече
                if (admins.Count == 0)
                {
                // Ако няма Админ, първият регистриран user → Admin
                    var firstUser = userManager.Users.OrderBy(u => u.CreatedOn).FirstOrDefault();
                    if (firstUser != null)
                    {
                        await userManager.AddToRoleAsync(firstUser, "Admin");
                    }
                }
            }

            app.MapRazorPages();

            app.Run();
        }
    }
}
