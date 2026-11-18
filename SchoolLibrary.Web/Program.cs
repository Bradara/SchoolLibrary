using Amazon.S3;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Middleware;
using SchoolLibrary.Data.Seeding;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services;
using SchoolLibrary.Services.Storage;

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
                .AddDefaultIdentity<ApplicationUser>(options =>
                {
                    options.SignIn.RequireConfirmedAccount = false;

                    // Too many wrong passwords lock the account for a few minutes (guessing passwords gets slow)
                    options.Lockout.AllowedForNewUsers = true;
                    options.Lockout.MaxFailedAccessAttempts = 5;
                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                })
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            builder.Services.AddControllersWithViews(options =>
                options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
            builder.Services.AddApplicationServices();

            // Where uploaded files are kept, chosen by FileStorage:Provider:
            //   "Oci"  -> an S3-compatible bucket (OCI Object Storage), so the files live off the server's disk;
            //   else   -> a folder: under the content root locally, or a mounted volume (FileStorage:LocalPath) in Docker.
            if (string.Equals(builder.Configuration["FileStorage:Provider"], "Oci", StringComparison.OrdinalIgnoreCase))
            {
                var objectStorage = builder.Configuration.GetSection("FileStorage:Oci").Get<ObjectStorageOptions>()
                    ?? new ObjectStorageOptions();
                objectStorage.Validate(); // fail fast at startup if a setting is missing

                builder.Services.AddSingleton<IAmazonS3>(_ => ObjectStorageClientFactory.Create(objectStorage));
                builder.Services.AddSingleton<IFileStorage>(sp =>
                    new OciObjectStorage(sp.GetRequiredService<IAmazonS3>(), objectStorage.Bucket));
            }
            else
            {
                var uploadsPath = builder.Configuration["FileStorage:LocalPath"];
                if (string.IsNullOrWhiteSpace(uploadsPath))
                {
                    uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "uploads");
                }
                builder.Services.AddSingleton<IFileStorage>(new LocalFileStorage(uploadsPath));
            }

            var app = builder.Build();

            // Must run first so Request.Scheme / RemoteIpAddress reflect the original client request.
            app.UseForwardedHeaders();

            // Protective headers on every response (framing, content type sniffing, referrer)
            app.UseSecurityHeaders();

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

            // In Production every cookie (sign-in, antiforgery, TempData) gets the Secure flag, so browsers never
            // send it over plain HTTP. Behind nginx the request is HTTPS (see UseForwardedHeaders above).
            // Security:AllowInsecureCookies=true is for the local Docker stack that is opened over http://localhost.
            if (app.Environment.IsProduction() && !app.Configuration.GetValue<bool>("Security:AllowInsecureCookies"))
            {
                app.UseCookiePolicy(new CookiePolicyOptions { Secure = CookieSecurePolicy.Always });
            }

            app.UseRouting();

            app.UseAuthorization();
            app.UseStatusCodePagesWithReExecute("/Home/Error", "?code={0}");

            app.MapControllerRoute(
                name: "areas",
                pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            // Apply migrations and seed grades, subjects and roles
            using (var scope = app.Services.CreateScope())
            {
                await ApplicationDbSeeder.SeedAsync(scope.ServiceProvider);

                // Demo accounts and data: never in Production (OCI, Docker), only on a developer machine
                // and only when switched on, e.g. with user secrets (Seed:DemoData, Seed:DemoPassword)
                if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Seed:DemoData"))
                {
                    var demoPassword = app.Configuration["Seed:DemoPassword"];
                    if (string.IsNullOrWhiteSpace(demoPassword))
                    {
                        app.Logger.LogWarning("Seed:DemoData is on, but Seed:DemoPassword is not set. The demo data was not added.");
                    }
                    else
                    {
                        var demo = await DemoDataSeeder.SeedAsync(scope.ServiceProvider, demoPassword);
                        app.Logger.LogInformation(
                            "Demo data: {Users} users, {Lessons} lessons, {Comments} comments, {Favorites} favorites, {Reports} reports added.",
                            demo.Users, demo.Lessons, demo.Comments, demo.Favorites, demo.Reports);
                    }
                }
            }

            app.MapRazorPages();

            app.Run();
        }
    }
}
