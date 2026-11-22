using System.Security.Claims;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Comments;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Favorites;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

/// <summary>The viewer built from a signed-in user, and the pure visibility rule.</summary>
public class ResourceViewerTests
{
    private static ClaimsPrincipal SignedIn(string id, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public void For_NotSignedIn_IsAGuest()
    {
        var viewer = ResourceViewer.For(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Null(viewer.UserId);
        Assert.Equal(ResourceAudience.Guests, viewer.Audience);
        Assert.False(viewer.IsAdministrator);
    }

    [Fact]
    public void For_Null_IsAGuest() => Assert.Same(ResourceViewer.Anonymous, ResourceViewer.For(null));

    [Theory]
    [InlineData(Roles.Student, ResourceAudience.Students)]
    [InlineData(Roles.Parent, ResourceAudience.Parents)]
    [InlineData(Roles.Teacher, ResourceAudience.Teachers)]
    public void For_ARole_AddsItsGroupToWhatGuestsSee(string role, ResourceAudience group)
    {
        var viewer = ResourceViewer.For(SignedIn("u1", Roles.User, role));

        Assert.Equal("u1", viewer.UserId);
        Assert.Equal(ResourceAudience.Guests | group, viewer.Audience);
        Assert.False(viewer.IsAdministrator);
    }

    [Fact]
    public void For_SeveralRoles_CombinesTheirGroups()
    {
        var viewer = ResourceViewer.For(SignedIn("u1", Roles.Teacher, Roles.Parent));

        Assert.Equal(ResourceAudience.Guests | ResourceAudience.Teachers | ResourceAudience.Parents, viewer.Audience);
    }

    [Fact]
    public void For_OnlyTheBaseRole_SeesWhatGuestsSee()
    {
        Assert.Equal(ResourceAudience.Guests, ResourceViewer.For(SignedIn("u1", Roles.User)).Audience);
    }

    [Fact]
    public void For_Administrator_IsMarked()
    {
        Assert.True(ResourceViewer.For(SignedIn("a1", Roles.Administrator)).IsAdministrator);
    }

    [Fact]
    public void Clean_DropsUnknownFlags()
    {
        Assert.Equal(ResourceAudience.Students, ResourceVisibility.Clean(ResourceAudience.Students | (ResourceAudience)32));
    }

    [Fact]
    public void Combine_JoinsTheTickedGroups_AndIgnoresTheHiddenNone()
    {
        var audience = ResourceVisibility.Combine(new[] { ResourceAudience.None, ResourceAudience.Students, ResourceAudience.Teachers });

        Assert.Equal(ResourceAudience.Students | ResourceAudience.Teachers, audience);
    }

    [Fact]
    public void Combine_NothingTicked_IsOnlyMe()
    {
        Assert.Equal(ResourceAudience.None, ResourceVisibility.Combine(new[] { ResourceAudience.None }));
        Assert.Equal(ResourceAudience.None, ResourceVisibility.Combine(null));
    }

    [Theory]
    [InlineData(ResourceAudience.Everyone, "Всички")]
    [InlineData(ResourceAudience.SignedIn, "Само регистрирани")]
    [InlineData(ResourceAudience.None, "Само аз")]
    [InlineData(ResourceAudience.Teachers, "Учители")]
    [InlineData(ResourceAudience.Students | ResourceAudience.Parents, "Ученици, Родители")]
    public void Describe_NamesTheAudienceInBulgarian(ResourceAudience audience, string expected)
    {
        Assert.Equal(expected, ResourceVisibility.Describe(audience));
    }

    [Fact]
    public void CanView_FollowsOwnerAdministratorAndGroups()
    {
        var resource = new Resource { OwnerId = "owner", Audience = ResourceAudience.Teachers };

        Assert.True(ResourceVisibility.CanView(resource, TestViewers.Teacher("t1")));
        Assert.True(ResourceVisibility.CanView(resource, TestViewers.Student("owner")));
        Assert.True(ResourceVisibility.CanView(resource, TestViewers.Administrator()));
        Assert.False(ResourceVisibility.CanView(resource, TestViewers.Student("s1")));
        Assert.False(ResourceVisibility.CanView(resource, ResourceViewer.Anonymous));
    }

    [Fact]
    public void CanView_AnonymousNeverMatchesAResourceWithoutOwner()
    {
        var resource = new Resource { OwnerId = null, Audience = ResourceAudience.None };

        Assert.False(ResourceVisibility.CanView(resource, ResourceViewer.Anonymous));
        Assert.True(ResourceVisibility.CanView(resource, TestViewers.Administrator()));
    }
}

/// <summary>Hidden resources in the catalog, details, files, favorites and comments.</summary>
public class ResourceVisibilityServiceTests
{
    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly ResourceService _service;

    private readonly Category _math;
    private readonly GradeLevel _grade5;
    private readonly License _license;

    public ResourceVisibilityServiceTests()
    {
        _service = new ResourceService(_context);

        _context.Users.Add(new ApplicationUser { Id = "owner", UserName = "owner", FirstName = "Иван", LastName = "Петров" });
        _context.Users.Add(new ApplicationUser { Id = "other", UserName = "other", FirstName = "Мария", LastName = "Георгиева" });
        _math = new Category { Name = "Математика" };
        _grade5 = new GradeLevel { Number = 5 };
        _license = new License { Name = "CC BY 4.0" };
        _context.AddRange(_math, _grade5, _license);
        _context.SaveChanges();
    }

    private async Task<Resource> AddAsync(string title, ResourceAudience audience, string? ownerId = "owner")
    {
        var resource = new Resource
        {
            Title = title,
            Author = "Автор",
            Url = "https://example.com/" + Guid.NewGuid().ToString("N"),
            OwnerId = ownerId,
            CreatedOn = DateTime.UtcNow,
            Audience = audience
        };
        resource.Categories.Add(_math);
        resource.GradeLevels.Add(_grade5);
        _context.Resources.Add(resource);
        await _context.SaveChangesAsync();
        return resource;
    }

    private async Task<List<string>> TitlesFor(ResourceViewer viewer) =>
        (await _service.SearchAsync(new ResourceFilter { Viewer = viewer, SortOrder = ResourceSortOrder.TitleAsc }))
            .Items.Select(r => r.Title).ToList();

    private async Task AddTheUsualSetAsync()
    {
        await AddAsync("1 Всички", ResourceAudience.Everyone);
        await AddAsync("2 Регистрирани", ResourceAudience.SignedIn);
        await AddAsync("3 Учители", ResourceAudience.Teachers);
        await AddAsync("4 Ученици", ResourceAudience.Students);
        await AddAsync("5 Само аз", ResourceAudience.None);
    }

    private ResourceInput Input(ResourceAudience audience) => new()
    {
        Title = "Дроби",
        Author = "Иван Петров",
        Url = "https://example.com/drobi",
        LicenseId = _license.Id,
        CategoryIds = new List<int> { _math.Id },
        GradeLevelIds = new List<int> { _grade5.Id },
        Audience = audience
    };

    // ---------- the catalog ----------

    [Fact]
    public async Task SearchAsync_Guest_SeesOnlyWhatIsOpenToGuests()
    {
        await AddTheUsualSetAsync();

        Assert.Equal(new[] { "1 Всички" }, await TitlesFor(ResourceViewer.Anonymous));
    }

    [Fact]
    public async Task SearchAsync_Student_SeesTheirGroupAndPublicOnes()
    {
        await AddTheUsualSetAsync();

        Assert.Equal(new[] { "1 Всички", "2 Регистрирани", "4 Ученици" }, await TitlesFor(TestViewers.Student("other")));
    }

    [Fact]
    public async Task SearchAsync_Teacher_SeesTheirGroupAndPublicOnes()
    {
        await AddTheUsualSetAsync();

        Assert.Equal(new[] { "1 Всички", "2 Регистрирани", "3 Учители" }, await TitlesFor(TestViewers.Teacher("other")));
    }

    [Fact]
    public async Task SearchAsync_Parent_DoesNotSeeTeacherOrStudentOnlyResources()
    {
        await AddTheUsualSetAsync();

        Assert.Equal(new[] { "1 Всички", "2 Регистрирани" }, await TitlesFor(TestViewers.Parent("other")));
    }

    [Fact]
    public async Task SearchAsync_Owner_SeesAllOwnResources()
    {
        await AddTheUsualSetAsync();

        Assert.Equal(5, (await TitlesFor(TestViewers.Parent("owner"))).Count);
    }

    [Fact]
    public async Task SearchAsync_Administrator_SeesEverything()
    {
        await AddTheUsualSetAsync();
        await AddAsync("6 Без собственик, скрит", ResourceAudience.None, ownerId: null);

        Assert.Equal(6, (await TitlesFor(TestViewers.Administrator())).Count);
    }

    [Fact]
    public async Task SearchAsync_CountsOnlyVisibleResources()
    {
        await AddTheUsualSetAsync();

        var result = await _service.SearchAsync(new ResourceFilter { Viewer = TestViewers.Student("other") });

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_WithoutViewer_TreatsTheCallerAsAGuest()
    {
        await AddTheUsualSetAsync();

        var result = await _service.SearchAsync(new ResourceFilter());

        Assert.Equal("1 Всички", Assert.Single(result.Items).Title);
    }

    [Fact]
    public async Task GetOwnersAsync_LeavesOutOwnersWhoseResourcesAreAllHidden()
    {
        await AddAsync("Скрит", ResourceAudience.Teachers, ownerId: "owner");
        await AddAsync("Публичен", ResourceAudience.Everyone, ownerId: "other");

        var owners = await _service.GetOwnersAsync(ResourceViewer.Anonymous);

        Assert.Equal("other", Assert.Single(owners).Id);
        Assert.Equal(2, (await _service.GetOwnersAsync(TestViewers.Teacher("t1"))).Count);
    }

    // ---------- details and files ----------

    [Fact]
    public async Task GetDetailsAsync_HiddenResource_LooksMissing()
    {
        var resource = await AddAsync("Учители", ResourceAudience.Teachers);

        Assert.Null(await _service.GetDetailsAsync(resource.Id, TestViewers.Student("other")));
        Assert.Null(await _service.GetDetailsAsync(resource.Id, ResourceViewer.Anonymous));
        Assert.NotNull(await _service.GetDetailsAsync(resource.Id, TestViewers.Teacher("other")));
        Assert.NotNull(await _service.GetDetailsAsync(resource.Id, TestViewers.Student("owner")));
        Assert.NotNull(await _service.GetDetailsAsync(resource.Id, TestViewers.Administrator()));
    }

    [Fact]
    public async Task CanViewAsync_FollowsTheSameRule()
    {
        var resource = await AddAsync("Ученици", ResourceAudience.Students);

        Assert.True(await _service.CanViewAsync(resource.Id, TestViewers.Student("other")));
        Assert.False(await _service.CanViewAsync(resource.Id, TestViewers.Teacher("other")));
        Assert.False(await _service.CanViewAsync(999, TestViewers.Administrator()));
    }

    [Fact]
    public async Task GetFileAsync_HiddenResource_ServesNoFile()
    {
        var resource = await AddAsync("Книга", ResourceAudience.Teachers);
        _context.ResourceFiles.Add(new ResourceFile
        {
            ResourceId = resource.Id,
            StorageKey = "resources/book.epub",
            FileName = "book.epub",
            ContentType = "application/epub+zip",
            Sha256 = new string('a', 64),
            UploadedOn = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        Assert.Null(await _service.GetFileAsync(resource.Id, ResourceViewer.Anonymous));
        Assert.Null(await _service.GetFileAsync(resource.Id, TestViewers.Student("other")));
        Assert.NotNull(await _service.GetFileAsync(resource.Id, TestViewers.Teacher("other")));
        Assert.NotNull(await _service.GetFileAsync(resource.Id, TestViewers.Student("owner")));
    }

    [Fact]
    public async Task GetForEditAsync_IsNotAffectedByTheAudience()
    {
        var resource = await AddAsync("Само аз", ResourceAudience.None);

        var result = await _service.GetForEditAsync(resource.Id, "owner", isAdministrator: false);

        Assert.True(result.Succeeded);
    }

    // ---------- create, update, set ----------

    [Fact]
    public async Task CreateAsync_DefaultInput_IsVisibleToEveryone()
    {
        var input = Input(ResourceAudience.Everyone);
        input.Audience = new ResourceInput().Audience;

        var result = await _service.CreateAsync(input, "owner");

        Assert.Equal(ResourceAudience.Everyone, result.Value!.Audience);
    }

    [Fact]
    public async Task CreateAsync_SavesTheChosenAudience_WithoutUnknownFlags()
    {
        var result = await _service.CreateAsync(Input(ResourceAudience.Teachers | (ResourceAudience)128), "owner");

        Assert.True(result.Succeeded);
        Assert.Equal(ResourceAudience.Teachers, _context.Resources.Single().Audience);
    }

    [Fact]
    public async Task UpdateAsync_ChangesTheAudience()
    {
        var created = await _service.CreateAsync(Input(ResourceAudience.Everyone), "owner");

        var result = await _service.UpdateAsync(created.Value!.Id, Input(ResourceAudience.None), "owner", isAdministrator: false);

        Assert.True(result.Succeeded);
        Assert.Equal(ResourceAudience.None, _context.Resources.Single().Audience);
    }

    [Fact]
    public async Task SetAudienceAsync_Owner_ChangesIt()
    {
        var resource = await AddAsync("Урок", ResourceAudience.Everyone);

        var result = await _service.SetAudienceAsync(resource.Id, ResourceAudience.Students | ResourceAudience.Parents, "owner", isAdministrator: false);

        Assert.True(result.Succeeded);
        Assert.Equal(ResourceAudience.Students | ResourceAudience.Parents, _context.Resources.Single().Audience);
    }

    [Fact]
    public async Task SetAudienceAsync_DropsUnknownFlags()
    {
        var resource = await AddAsync("Урок", ResourceAudience.Everyone);

        await _service.SetAudienceAsync(resource.Id, (ResourceAudience)(-1), "owner", isAdministrator: false);

        Assert.Equal(ResourceAudience.Everyone, _context.Resources.Single().Audience);
    }

    [Fact]
    public async Task SetAudienceAsync_AnotherUser_IsForbidden()
    {
        var resource = await AddAsync("Урок", ResourceAudience.Everyone);

        var result = await _service.SetAudienceAsync(resource.Id, ResourceAudience.None, "other", isAdministrator: false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
        Assert.Equal(ResourceAudience.Everyone, _context.Resources.Single().Audience);
    }

    [Fact]
    public async Task SetAudienceAsync_AdministratorOnSomeoneElsesResource_IsForbidden()
    {
        // Administrators see everything, but the choice of audience belongs to the author
        var resource = await AddAsync("Урок", ResourceAudience.Everyone);

        var result = await _service.SetAudienceAsync(resource.Id, ResourceAudience.None, "admin", isAdministrator: true);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task SetAudienceAsync_AdministratorOnAResourceWithoutOwner_ChangesIt()
    {
        var resource = await AddAsync("Свободен", ResourceAudience.Everyone, ownerId: null);

        var result = await _service.SetAudienceAsync(resource.Id, ResourceAudience.SignedIn, "admin", isAdministrator: true);

        Assert.True(result.Succeeded);
        Assert.Equal(ResourceAudience.SignedIn, _context.Resources.Single().Audience);
    }

    [Fact]
    public async Task SetAudienceAsync_UnknownResource_IsNotFound()
    {
        var result = await _service.SetAudienceAsync(999, ResourceAudience.None, "owner", isAdministrator: false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    // ---------- favorites and comments ----------

    [Fact]
    public async Task Favorites_HiddenResource_CannotBeSaved()
    {
        var resource = await AddAsync("Учители", ResourceAudience.Teachers);
        var favorites = new FavoriteService(_context);

        var result = await favorites.AddAsync(TestViewers.Student("other"), resource.Id);

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Empty(_context.Favorites);
    }

    [Fact]
    public async Task Favorites_WithoutUser_CannotBeSaved()
    {
        var resource = await AddAsync("Всички", ResourceAudience.Everyone);

        var result = await new FavoriteService(_context).AddAsync(ResourceViewer.Anonymous, resource.Id);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task Favorites_ResourceHiddenAfterSaving_IsLeftOutOfTheList()
    {
        var open = await AddAsync("Отворен", ResourceAudience.Everyone);
        var later = await AddAsync("Скрит по-късно", ResourceAudience.Everyone);
        var favorites = new FavoriteService(_context);
        await favorites.AddAsync(TestViewers.Student("other"), open.Id);
        await favorites.AddAsync(TestViewers.Student("other"), later.Id);

        later.Audience = ResourceAudience.Teachers;
        await _context.SaveChangesAsync();

        var list = await favorites.GetFavoritesAsync(TestViewers.Student("other"), 1);

        Assert.Equal("Отворен", Assert.Single(list.Items).Title);
        Assert.Equal(1, list.TotalCount);
    }

    [Fact]
    public async Task Comments_HiddenResource_CannotBeCommented()
    {
        var resource = await AddAsync("Учители", ResourceAudience.Teachers);
        var comments = new CommentService(_context);

        var refused = await comments.AddAsync(resource.Id, TestViewers.Parent("other"), "Здравейте");
        var allowed = await comments.AddAsync(resource.Id, TestViewers.Teacher("other"), "Здравейте");

        Assert.Equal(ServiceError.NotFound, refused.Error);
        Assert.True(allowed.Succeeded);
    }
}
