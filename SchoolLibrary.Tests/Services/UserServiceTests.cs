using Microsoft.AspNetCore.Identity;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Users;

namespace SchoolLibrary.Tests.Services;

public class UserServiceTests
{
    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly UserService _service;

    public UserServiceTests()
    {
        _service = new UserService(_context);
        foreach (var role in Roles.All)
        {
            _context.Roles.Add(new IdentityRole { Id = role, Name = role });
        }
        _context.SaveChanges();
    }

    private async Task<ApplicationUser> AddUserAsync(
        string id, string first = "Иван", string last = "Петров",
        bool teacherRequested = false, DateTime? createdOn = null, params string[] roles)
    {
        var user = new ApplicationUser
        {
            Id = id,
            UserName = id,
            Email = $"{id}@example.com",
            FirstName = first,
            LastName = last,
            TeacherRequested = teacherRequested,
            CreatedOn = createdOn ?? DateTime.UtcNow
        };
        _context.Users.Add(user);
        foreach (var role in roles)
        {
            _context.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = role });
        }
        await _context.SaveChangesAsync();
        return user;
    }

    private IEnumerable<string> RolesOf(string userId) =>
        _context.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToList();

    // ---- SearchAsync ----

    [Fact]
    public async Task SearchAsync_ReturnsUsersWithTheirRoles_OldestFirst()
    {
        var now = DateTime.UtcNow;
        await AddUserAsync("new", createdOn: now, roles: new[] { Roles.User, Roles.Student });
        await AddUserAsync("old", createdOn: now.AddDays(-1), roles: new[] { Roles.User, Roles.Administrator });

        var result = await _service.SearchAsync(new UserFilter());

        Assert.Equal(new[] { "old", "new" }, result.Items.Select(u => u.Id));
        Assert.Equal(new[] { Roles.Administrator, Roles.User }, result.Items[0].Roles);
    }

    [Theory]
    [InlineData("мария")]
    [InlineData("ИВАНОВА")]
    [InlineData("мария иванова")]
    [InlineData("MARIA@")]
    public async Task SearchAsync_SearchText_MatchesNameOrEmailIgnoringCase(string text)
    {
        await AddUserAsync("maria", "Мария", "Иванова");
        await AddUserAsync("other", "Георги", "Димитров");
        _context.Users.Single(u => u.Id == "maria").Email = "maria@example.com";
        await _context.SaveChangesAsync();

        var result = await _service.SearchAsync(new UserFilter { SearchText = text });

        Assert.Equal("maria", Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task SearchAsync_OnlyPendingTeachers_FiltersRequests()
    {
        await AddUserAsync("t1", teacherRequested: true);
        await AddUserAsync("s1");

        var result = await _service.SearchAsync(new UserFilter { OnlyPendingTeachers = true });

        Assert.Equal("t1", Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task SearchAsync_PagesAndClampsPageNumber()
    {
        for (var i = 0; i < 12; i++)
        {
            await AddUserAsync($"u{i:00}", createdOn: DateTime.UtcNow.AddMinutes(i));
        }

        var second = await _service.SearchAsync(new UserFilter { Page = 2, PageSize = 5 });
        var beyond = await _service.SearchAsync(new UserFilter { Page = 99, PageSize = 5 });

        Assert.Equal(12, second.TotalCount);
        Assert.Equal(new[] { "u05", "u06", "u07", "u08", "u09" }, second.Items.Select(u => u.Id));
        Assert.Equal(3, beyond.Page);
        Assert.Equal(2, beyond.Items.Count);
    }

    // ---- ApproveTeacherAsync ----

    [Fact]
    public async Task ApproveTeacherAsync_GivesTeacherRoleInsteadOfStudent()
    {
        await AddUserAsync("t1", teacherRequested: true, roles: new[] { Roles.User, Roles.Student });

        var result = await _service.ApproveTeacherAsync("t1");

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.TeacherRequested);
        Assert.Equal(new[] { Roles.Teacher, Roles.User }, result.Value.Roles);
        Assert.False(_context.Users.Single().TeacherRequested);
    }

    [Fact]
    public async Task ApproveTeacherAsync_UnknownUser_ReturnsNotFound()
    {
        var result = await _service.ApproveTeacherAsync("missing");

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task ApproveTeacherAsync_WithoutRequest_ReturnsInvalid()
    {
        await AddUserAsync("s1", roles: Roles.Student);

        var result = await _service.ApproveTeacherAsync("s1");

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.DoesNotContain(Roles.Teacher, RolesOf("s1"));
    }

    [Fact]
    public async Task ApproveTeacherAsync_MissingTeacherRole_ReturnsInvalid()
    {
        _context.Roles.Remove(_context.Roles.Single(r => r.Name == Roles.Teacher));
        await AddUserAsync("t1", teacherRequested: true);

        var result = await _service.ApproveTeacherAsync("t1");

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.True(_context.Users.Single().TeacherRequested);
    }

    // ---- RejectTeacherAsync ----

    [Fact]
    public async Task RejectTeacherAsync_ClearsRequestAndKeepsStudentRole()
    {
        await AddUserAsync("t1", teacherRequested: true, roles: Roles.Student);

        var result = await _service.RejectTeacherAsync("t1");

        Assert.True(result.Succeeded);
        Assert.False(_context.Users.Single().TeacherRequested);
        Assert.Equal(new[] { Roles.Student }, RolesOf("t1"));
    }

    [Fact]
    public async Task RejectTeacherAsync_UnknownUser_ReturnsNotFound()
    {
        Assert.Equal(ServiceError.NotFound, (await _service.RejectTeacherAsync("missing")).Error);
    }

    [Fact]
    public async Task RejectTeacherAsync_WithoutRequest_ReturnsInvalid()
    {
        await AddUserAsync("s1");

        Assert.Equal(ServiceError.Invalid, (await _service.RejectTeacherAsync("s1")).Error);
    }

    // ---- SetRoleAsync ----

    [Theory]
    [InlineData(Roles.Administrator)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Student)]
    [InlineData(Roles.Parent)]
    public async Task SetRoleAsync_Grant_AddsRoleOnce(string role)
    {
        await AddUserAsync("u1", roles: Roles.User);

        await _service.SetRoleAsync("u1", role, true, "admin");
        var again = await _service.SetRoleAsync("u1", role, true, "admin");

        Assert.True(again.Succeeded);
        Assert.Single(RolesOf("u1"), role);
        Assert.Contains(Roles.User, RolesOf("u1"));
    }

    [Fact]
    public async Task SetRoleAsync_Revoke_RemovesOnlyThatRole()
    {
        await AddUserAsync("u1", roles: new[] { Roles.User, Roles.Student, Roles.Parent });

        var result = await _service.SetRoleAsync("u1", Roles.Parent, false, "admin");

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { Roles.Student, Roles.User }, result.Value!.Roles);
    }

    [Fact]
    public async Task SetRoleAsync_GrantTeacher_ClearsPendingRequest()
    {
        await AddUserAsync("u1", teacherRequested: true, roles: Roles.Student);

        await _service.SetRoleAsync("u1", Roles.Teacher, true, "admin");

        Assert.False(_context.Users.Single().TeacherRequested);
    }

    [Fact]
    public async Task SetRoleAsync_RevokeOwnAdministrator_ReturnsForbidden()
    {
        await AddUserAsync("me", roles: Roles.Administrator);

        var result = await _service.SetRoleAsync("me", Roles.Administrator, false, "me");

        Assert.Equal(ServiceError.Forbidden, result.Error);
        Assert.Contains(Roles.Administrator, RolesOf("me"));
    }

    [Fact]
    public async Task SetRoleAsync_RevokeOwnOtherRole_Succeeds()
    {
        await AddUserAsync("me", roles: new[] { Roles.Administrator, Roles.Teacher });

        var result = await _service.SetRoleAsync("me", Roles.Teacher, false, "me");

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(Roles.User)]
    [InlineData("Hacker")]
    [InlineData("")]
    public async Task SetRoleAsync_NotAssignableRole_ReturnsInvalid(string role)
    {
        await AddUserAsync("u1");

        var result = await _service.SetRoleAsync("u1", role, true, "admin");

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task SetRoleAsync_UnknownUser_ReturnsNotFound()
    {
        Assert.Equal(ServiceError.NotFound, (await _service.SetRoleAsync("x", Roles.Student, true, "admin")).Error);
    }

    [Fact]
    public async Task SetRoleAsync_MissingRoleInDatabase_ReturnsInvalid()
    {
        _context.Roles.Remove(_context.Roles.Single(r => r.Name == Roles.Parent));
        await AddUserAsync("u1");

        Assert.Equal(ServiceError.Invalid, (await _service.SetRoleAsync("u1", Roles.Parent, true, "admin")).Error);
    }
}
