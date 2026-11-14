using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Data.Seeding;

namespace SchoolLibrary.Tests.Services;

// The seed runs on every start of the application, also on the production database: what it does to the
// data that is already there matters as much as what it adds to an empty database.
public class ApplicationDbSeederTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole> _roles;

    public ApplicationDbSeederTests()
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
        _users = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _roles = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    private Task SeedAsync() => ApplicationDbSeeder.SeedDataAsync(_scope.ServiceProvider);

    private async Task<ApplicationUser> AddUserAsync(string name, DateTime createdOn, params string[] roles)
    {
        var user = new ApplicationUser { UserName = name, Email = name + "@example.com", FirstName = name, LastName = "Test", CreatedOn = createdOn };
        await _users.CreateAsync(user);
        foreach (var role in roles)
        {
            if (!await _roles.RoleExistsAsync(role))
            {
                await _roles.CreateAsync(new IdentityRole(role));
            }
            await _users.AddToRoleAsync(user, role);
        }
        return user;
    }

    // ---------- reference data ----------

    [Fact]
    public async Task SeedDataAsync_EmptyDatabase_AddsGradesSubjectsLicensesAndRoles()
    {
        await SeedAsync();

        Assert.Equal(Enum.GetValues<Grades>().Length, _db.GradeLevels.Count());
        Assert.Equal(new[] { 5, 6, 7, 8, 9, 10, 11, 12 }, _db.GradeLevels.Select(g => g.Number).OrderBy(n => n));
        Assert.True(_db.Categories.Count() >= 30);
        Assert.Contains(_db.Categories, c => c.Name == "Математика");
        Assert.Contains(_db.Categories, c => c.Name == "Човекът и природата");
        Assert.Equal(14, _db.Licenses.Count());
        Assert.Contains(_db.Licenses, l => l.Name == "CC BY-NC 4.0");
        Assert.Equal(Roles.All.Length, _db.Roles.Count());
    }

    [Fact]
    public async Task SeedDataAsync_RunTwice_AddsNothingTheSecondTime()
    {
        await SeedAsync();
        var counts = (_db.GradeLevels.Count(), _db.Categories.Count(), _db.Licenses.Count(), _db.Roles.Count());

        await SeedAsync();

        Assert.Equal(counts, (_db.GradeLevels.Count(), _db.Categories.Count(), _db.Licenses.Count(), _db.Roles.Count()));
    }

    [Fact]
    public async Task SeedDataAsync_LicenseNamesAreUnique_AndHaveAnAddressOnlyWhenThereIsATextOnline()
    {
        await SeedAsync();

        var licenses = _db.Licenses.ToList();
        Assert.Equal(licenses.Count, licenses.Select(l => l.Name).Distinct().Count());
        Assert.All(licenses.Where(l => l.Url != null), l => Assert.StartsWith("https://", l.Url));
        Assert.All(licenses, l => Assert.False(string.IsNullOrWhiteSpace(l.Description)));
    }

    [Fact]
    public async Task SeedDataAsync_SubjectsAdministratorsRemoved_AreNotAddedBack()
    {
        await SeedAsync();
        var removed = _db.Categories.First(c => c.Name == "Религия");
        _db.Categories.Remove(removed);
        await _db.SaveChangesAsync();

        await SeedAsync();

        Assert.DoesNotContain(_db.Categories, c => c.Name == "Религия");
    }

    [Fact]
    public async Task SeedDataAsync_OnlyTheMissingLicensesAreAdded()
    {
        _db.Licenses.Add(new License { Name = "CC BY 4.0", Description = "моето описание" });
        await _db.SaveChangesAsync();

        await SeedAsync();

        Assert.Equal(14, _db.Licenses.Count());
        Assert.Equal("моето описание", _db.Licenses.Single(l => l.Name == "CC BY 4.0").Description);
    }

    // ---------- roles of the existing users ----------

    [Fact]
    public async Task SeedDataAsync_RenamesTheOldAdminRole_SoItsUsersKeepIt()
    {
        var oldAdmin = await AddUserAsync("boss", DateTime.UtcNow.AddDays(-10), Roles.LegacyAdmin);

        await SeedAsync();

        Assert.False(await _roles.RoleExistsAsync(Roles.LegacyAdmin));
        Assert.True(await _users.IsInRoleAsync(oldAdmin, Roles.Administrator));
        Assert.Equal(Roles.All.Length, _db.Roles.Count());
    }

    [Fact]
    public async Task SeedDataAsync_WhenBothAdminRolesExist_TheOldOneIsLeftAlone()
    {
        await AddUserAsync("boss", DateTime.UtcNow.AddDays(-10), Roles.LegacyAdmin, Roles.Administrator);

        await SeedAsync();

        Assert.True(await _roles.RoleExistsAsync(Roles.LegacyAdmin));
        Assert.True(await _roles.RoleExistsAsync(Roles.Administrator));
    }

    [Fact]
    public async Task SeedDataAsync_WithoutAnyAdministrator_TheOldestUserBecomesOne()
    {
        var oldest = await AddUserAsync("oldest", DateTime.UtcNow.AddDays(-30));
        var newer = await AddUserAsync("newer", DateTime.UtcNow.AddDays(-1));

        await SeedAsync();

        Assert.True(await _users.IsInRoleAsync(oldest, Roles.Administrator));
        Assert.False(await _users.IsInRoleAsync(newer, Roles.Administrator));
    }

    [Fact]
    public async Task SeedDataAsync_WithAnAdministrator_NobodyElseBecomesOne()
    {
        var admin = await AddUserAsync("admin", DateTime.UtcNow.AddDays(-1), Roles.Administrator);
        var older = await AddUserAsync("older", DateTime.UtcNow.AddDays(-30));

        await SeedAsync();

        Assert.True(await _users.IsInRoleAsync(admin, Roles.Administrator));
        Assert.False(await _users.IsInRoleAsync(older, Roles.Administrator));
    }

    [Fact]
    public async Task SeedDataAsync_EveryUserGetsTheBaseRole()
    {
        var student = await AddUserAsync("student", DateTime.UtcNow, Roles.Student);
        var plain = await AddUserAsync("plain", DateTime.UtcNow);

        await SeedAsync();

        Assert.True(await _users.IsInRoleAsync(student, Roles.User));
        Assert.True(await _users.IsInRoleAsync(plain, Roles.User));
    }

    [Fact]
    public async Task SeedDataAsync_UsersFromBeforeTheRoles_BecomeTeachers_ButOthersKeepTheirRole()
    {
        var old = await AddUserAsync("old", DateTime.UtcNow.AddDays(-5), Roles.Administrator);
        var legacy = await AddUserAsync("legacy", DateTime.UtcNow.AddDays(-4));
        var student = await AddUserAsync("student", DateTime.UtcNow.AddDays(-3), Roles.Student);
        var parent = await AddUserAsync("parent", DateTime.UtcNow.AddDays(-2), Roles.Parent);

        await SeedAsync();

        Assert.True(await _users.IsInRoleAsync(legacy, Roles.Teacher));
        Assert.False(await _users.IsInRoleAsync(old, Roles.Teacher));
        Assert.False(await _users.IsInRoleAsync(student, Roles.Teacher));
        Assert.False(await _users.IsInRoleAsync(parent, Roles.Teacher));
    }

    [Fact]
    public async Task SeedDataAsync_RunTwice_DoesNotChangeTheRolesAgain()
    {
        await AddUserAsync("a", DateTime.UtcNow.AddDays(-5));
        await AddUserAsync("b", DateTime.UtcNow.AddDays(-4), Roles.Student);
        await SeedAsync();
        var userRoles = _db.UserRoles.Count();

        await SeedAsync();

        Assert.Equal(userRoles, _db.UserRoles.Count());
    }

    // ---------- links of the resources that existed before NormalizedUrl ----------

    [Fact]
    public async Task SeedDataAsync_FillsTheNormalizedUrlOfOlderResources()
    {
        _db.Resources.AddRange(
            new Resource { Title = "A", Author = "x", Url = "http://www.example.com/page/?utm_source=a", CreatedOn = DateTime.UtcNow },
            new Resource { Title = "B", Author = "x", Url = "https://youtu.be/dQw4w9WgXcQ", CreatedOn = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await SeedAsync();

        Assert.Equal("https://example.com/page", _db.Resources.Single(r => r.Title == "A").NormalizedUrl);
        Assert.Equal("https://youtube.com/watch?v=dQw4w9WgXcQ", _db.Resources.Single(r => r.Title == "B").NormalizedUrl);
    }

    [Fact]
    public async Task SeedDataAsync_ALinkThatCannotBeNormalized_IsKeptAsTyped()
    {
        _db.Resources.Add(new Resource { Title = "Стар", Author = "x", Url = "not a link", CreatedOn = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await SeedAsync();

        Assert.Equal("not a link", _db.Resources.Single().NormalizedUrl);
    }

    [Fact]
    public async Task SeedDataAsync_DoesNotOverwriteAnExistingNormalizedUrl()
    {
        _db.Resources.Add(new Resource { Title = "A", Author = "x", Url = "https://example.com/a", NormalizedUrl = "kept", CreatedOn = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await SeedAsync();

        Assert.Equal("kept", _db.Resources.Single().NormalizedUrl);
    }

    [Fact]
    public async Task SeedDataAsync_ResourcesSharingALink_AreReportedButNotChanged()
    {
        _db.Resources.AddRange(
            new Resource { Title = "A", Author = "x", Url = "https://example.com/same", CreatedOn = DateTime.UtcNow },
            new Resource { Title = "B", Author = "x", Url = "http://www.example.com/same/", CreatedOn = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        await SeedAsync();

        Assert.Equal(2, _db.Resources.Count());
        Assert.All(_db.Resources, r => Assert.Equal("https://example.com/same", r.NormalizedUrl));
    }
}
