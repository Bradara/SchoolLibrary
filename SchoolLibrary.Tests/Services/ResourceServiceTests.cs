using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

public class ResourceServiceTests
{
    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly ResourceService _service;

    private readonly ApplicationUser _ivan;
    private readonly ApplicationUser _maria;
    private readonly Category _math;
    private readonly Category _music;
    private readonly GradeLevel _grade5;
    private readonly GradeLevel _grade6;
    private readonly License _license;

    public ResourceServiceTests()
    {
        _service = new ResourceService(_context);

        _ivan = AddUser("ivan", "Иван", "Петров");
        _maria = AddUser("maria", "Мария", "Георгиева");
        _math = AddCategory("Математика");
        _music = AddCategory("Музика");
        _grade5 = AddGrade(5);
        _grade6 = AddGrade(6);
        _license = new License { Name = "CC BY 4.0" };
        _context.Licenses.Add(_license);
        _context.SaveChanges();
    }

    // ---------- helpers ----------

    private ApplicationUser AddUser(string id, string firstName, string lastName)
    {
        var user = new ApplicationUser { Id = id, UserName = id, FirstName = firstName, LastName = lastName };
        _context.Users.Add(user);
        return user;
    }

    private Category AddCategory(string name)
    {
        var category = new Category { Name = name };
        _context.Categories.Add(category);
        return category;
    }

    private GradeLevel AddGrade(int number)
    {
        var grade = new GradeLevel { Number = number };
        _context.GradeLevels.Add(grade);
        return grade;
    }

    private async Task<Resource> AddResourceAsync(
        string title,
        string author = "Автор",
        ApplicationUser? owner = null,
        Category? category = null,
        GradeLevel? grade = null,
        DateTime? createdOn = null)
    {
        var resource = new Resource
        {
            Title = title,
            Author = author,
            Url = "https://example.com",
            OwnerId = (owner ?? _ivan).Id,
            CreatedOn = createdOn ?? DateTime.UtcNow
        };
        resource.Categories.Add(category ?? _math);
        resource.GradeLevels.Add(grade ?? _grade5);
        _context.Resources.Add(resource);
        await _context.SaveChangesAsync();
        return resource;
    }

    private ResourceInput ValidInput() => new()
    {
        Title = "Дроби",
        Author = "Иван Петров",
        Source = "Учебник",
        Description = "Урок по дроби",
        Url = "https://example.com/drobi",
        LicenseId = _license.Id,
        CategoryIds = new List<int> { _math.Id },
        GradeLevelIds = new List<int> { _grade5.Id }
    };

    // ---------- SearchAsync: filters ----------

    [Fact]
    public async Task SearchAsync_WithoutFilter_ReturnsAllNewestFirst()
    {
        await AddResourceAsync("Стар", createdOn: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddResourceAsync("Нов", createdOn: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await _service.SearchAsync(new ResourceFilter());

        Assert.Equal(new[] { "Нов", "Стар" }, result.Items.Select(r => r.Title));
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_WithNoResources_ReturnsEmptyFirstPage()
    {
        var result = await _service.SearchAsync(new ResourceFilter());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(0, result.TotalPages);
    }

    [Theory]
    [InlineData("дроби")]
    [InlineData("ДРОБИ")]
    [InlineData("  Дроби ")]
    [InlineData("роб")]
    public async Task SearchAsync_SearchText_MatchesTitleIgnoringCase(string text)
    {
        await AddResourceAsync("Дроби");
        await AddResourceAsync("Геометрия");

        var result = await _service.SearchAsync(new ResourceFilter { SearchText = text });

        Assert.Equal(new[] { "Дроби" }, result.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task SearchAsync_SearchText_MatchesAuthor()
    {
        await AddResourceAsync("Първи", author: "Георги Димитров");
        await AddResourceAsync("Втори", author: "Мария Иванова");

        var result = await _service.SearchAsync(new ResourceFilter { SearchText = "димитров" });

        Assert.Equal(new[] { "Първи" }, result.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task SearchAsync_BlankSearchText_IsIgnored()
    {
        await AddResourceAsync("Дроби");
        await AddResourceAsync("Геометрия");

        var result = await _service.SearchAsync(new ResourceFilter { SearchText = "   " });

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_CategoryFilter_ReturnsOnlyThatSubject()
    {
        await AddResourceAsync("Дроби", category: _math);
        await AddResourceAsync("Ноти", category: _music);

        var result = await _service.SearchAsync(new ResourceFilter { CategoryId = _music.Id });

        Assert.Equal(new[] { "Ноти" }, result.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task SearchAsync_CategoryFilter_MatchesResourceWithSeveralSubjects()
    {
        var resource = await AddResourceAsync("Ритъм и числа", category: _math);
        resource.Categories.Add(_music);
        await _context.SaveChangesAsync();

        var byMusic = await _service.SearchAsync(new ResourceFilter { CategoryId = _music.Id });
        var byMath = await _service.SearchAsync(new ResourceFilter { CategoryId = _math.Id });

        Assert.Single(byMusic.Items);
        Assert.Single(byMath.Items);
    }

    [Fact]
    public async Task SearchAsync_GradeFilter_ReturnsOnlyThatGrade()
    {
        await AddResourceAsync("Петокласници", grade: _grade5);
        await AddResourceAsync("Шестокласници", grade: _grade6);

        var result = await _service.SearchAsync(new ResourceFilter { GradeLevelId = _grade6.Id });

        Assert.Equal(new[] { "Шестокласници" }, result.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task SearchAsync_OwnerFilter_ReturnsOnlyThatUsersResources()
    {
        await AddResourceAsync("Ивански", owner: _ivan);
        await AddResourceAsync("Мариин", owner: _maria);

        var result = await _service.SearchAsync(new ResourceFilter { OwnerId = _maria.Id });

        Assert.Equal(new[] { "Мариин" }, result.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task SearchAsync_DateRange_IncludesBothBoundaryDays()
    {
        await AddResourceAsync("Преди", createdOn: new DateTime(2026, 5, 9, 23, 59, 0, DateTimeKind.Utc));
        await AddResourceAsync("Първи ден", createdOn: new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc));
        await AddResourceAsync("Последен ден", createdOn: new DateTime(2026, 5, 12, 23, 59, 0, DateTimeKind.Utc));
        await AddResourceAsync("След", createdOn: new DateTime(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc));

        var result = await _service.SearchAsync(new ResourceFilter
        {
            FromDate = new DateTime(2026, 5, 10),
            ToDate = new DateTime(2026, 5, 12)
        });

        Assert.Equal(new[] { "Последен ден", "Първи ден" }, result.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task SearchAsync_LoadsSubjectsGradesAndOwner()
    {
        await AddResourceAsync("Дроби", owner: _maria, category: _music, grade: _grade6);

        var item = (await _service.SearchAsync(new ResourceFilter())).Items.Single();

        Assert.Equal("Музика", item.Categories.Single().Name);
        Assert.Equal(6, item.GradeLevels.Single().Number);
        Assert.Equal("Мария", item.Owner!.FirstName);
    }

    // ---------- SearchAsync: sorting ----------

    [Theory]
    [InlineData(ResourceSortOrder.DateAsc, new[] { "Б", "В", "А" })]
    [InlineData(ResourceSortOrder.DateDesc, new[] { "А", "В", "Б" })]
    [InlineData(ResourceSortOrder.TitleAsc, new[] { "А", "Б", "В" })]
    [InlineData(ResourceSortOrder.TitleDesc, new[] { "В", "Б", "А" })]
    public async Task SearchAsync_SortsByTitleAndDate(ResourceSortOrder order, string[] expected)
    {
        await AddResourceAsync("А", createdOn: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddResourceAsync("Б", createdOn: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await AddResourceAsync("В", createdOn: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await _service.SearchAsync(new ResourceFilter { SortOrder = order });

        Assert.Equal(expected, result.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task SearchAsync_TitleSort_IgnoresCase()
    {
        await AddResourceAsync("вторник");
        await AddResourceAsync("Анализ");
        await AddResourceAsync("Бележки");

        var result = await _service.SearchAsync(new ResourceFilter { SortOrder = ResourceSortOrder.TitleAsc });

        Assert.Equal(new[] { "Анализ", "Бележки", "вторник" }, result.Items.Select(r => r.Title));
    }

    [Theory]
    [InlineData(ResourceSortOrder.AuthorAsc, new[] { "Първи", "Втори" })]
    [InlineData(ResourceSortOrder.AuthorDesc, new[] { "Втори", "Първи" })]
    public async Task SearchAsync_SortsByAuthor(ResourceSortOrder order, string[] expected)
    {
        await AddResourceAsync("Втори", author: "Яна");
        await AddResourceAsync("Първи", author: "Ангел");

        var result = await _service.SearchAsync(new ResourceFilter { SortOrder = order });

        Assert.Equal(expected, result.Items.Select(r => r.Title));
    }

    [Fact]
    public async Task SearchAsync_SortsByOwnerName()
    {
        await AddResourceAsync("От Мария", owner: _maria);
        await AddResourceAsync("От Иван", owner: _ivan);

        var result = await _service.SearchAsync(new ResourceFilter { SortOrder = ResourceSortOrder.OwnerAsc });

        Assert.Equal(new[] { "От Иван", "От Мария" }, result.Items.Select(r => r.Title));
    }

    // ---------- SearchAsync: paging ----------

    private async Task AddManyAsync(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await AddResourceAsync($"Ресурс {i:00}", createdOn: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i));
        }
    }

    [Fact]
    public async Task SearchAsync_Paging_SplitsIntoPages()
    {
        await AddManyAsync(25);

        var first = await _service.SearchAsync(new ResourceFilter { Page = 1, PageSize = 10 });
        var last = await _service.SearchAsync(new ResourceFilter { Page = 3, PageSize = 10 });

        Assert.Equal(10, first.Items.Count);
        Assert.Equal(25, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.False(first.HasPrevious);
        Assert.True(first.HasNext);
        Assert.Equal(5, last.Items.Count);
        Assert.True(last.HasPrevious);
        Assert.False(last.HasNext);
    }

    [Fact]
    public async Task SearchAsync_Paging_PagesDoNotOverlap()
    {
        await AddManyAsync(25);

        var titles = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await _service.SearchAsync(new ResourceFilter { Page = page, PageSize = 10 });
            titles.AddRange(result.Items.Select(r => r.Title));
        }

        Assert.Equal(25, titles.Distinct().Count());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(99, 3)]
    public async Task SearchAsync_Paging_ClampsPageIntoValidRange(int requested, int expectedPage)
    {
        await AddManyAsync(25);

        var result = await _service.SearchAsync(new ResourceFilter { Page = requested, PageSize = 10 });

        Assert.Equal(expectedPage, result.Page);
        Assert.NotEmpty(result.Items);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(100000, ResourceFilter.MaxPageSize)]
    public async Task SearchAsync_Paging_ClampsPageSize(int requested, int expected)
    {
        await AddManyAsync(3);

        var result = await _service.SearchAsync(new ResourceFilter { PageSize = requested });

        Assert.Equal(expected, result.PageSize);
    }

    // ---------- GetOwnersAsync ----------

    [Fact]
    public async Task GetOwnersAsync_ReturnsOnlyUsersWithResources_OrderedByName()
    {
        AddUser("petar", "Петър", "Без ресурси");
        await AddResourceAsync("Едно", owner: _maria);
        await AddResourceAsync("Две", owner: _ivan);
        await AddResourceAsync("Три", owner: _ivan);

        var owners = await _service.GetOwnersAsync();

        Assert.Equal(new[] { "Иван Петров", "Мария Георгиева" }, owners.Select(o => o.FullName));
    }

    // ---------- GetDetailsAsync ----------

    [Fact]
    public async Task GetDetailsAsync_ReturnsResourceWithRelations()
    {
        var resource = await AddResourceAsync("Дроби", owner: _maria, category: _music, grade: _grade6);

        var result = await _service.GetDetailsAsync(resource.Id);

        Assert.NotNull(result);
        Assert.Equal("Музика", result!.Categories.Single().Name);
        Assert.Equal(6, result.GradeLevels.Single().Number);
        Assert.Equal("maria", result.Owner!.Id);
    }

    [Fact]
    public async Task GetDetailsAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _service.GetDetailsAsync(999));
    }

    // ---------- GetForEditAsync / GetForDeleteAsync ----------

    [Fact]
    public async Task GetForEditAsync_Owner_Succeeds()
    {
        var resource = await AddResourceAsync("Дроби", owner: _ivan);

        var result = await _service.GetForEditAsync(resource.Id, _ivan.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(resource.Id, result.Value!.Id);
    }

    [Fact]
    public async Task GetForEditAsync_AnotherUser_IsForbidden()
    {
        var resource = await AddResourceAsync("Дроби", owner: _ivan);

        var result = await _service.GetForEditAsync(resource.Id, _maria.Id);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task GetForEditAsync_UnknownId_ReturnsNotFound()
    {
        var result = await _service.GetForEditAsync(999, _ivan.Id);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Theory]
    [InlineData("ivan", false, true)]    // owner
    [InlineData("maria", true, true)]    // administrator, not the owner
    [InlineData("maria", false, false)]  // somebody else
    public async Task GetForDeleteAsync_ChecksOwnerOrAdministrator(string userId, bool isAdministrator, bool allowed)
    {
        var resource = await AddResourceAsync("Дроби", owner: _ivan);

        var result = await _service.GetForDeleteAsync(resource.Id, userId, isAdministrator);

        Assert.Equal(allowed, result.Succeeded);
        if (!allowed)
        {
            Assert.Equal(ServiceError.Forbidden, result.Error);
        }
    }

    [Fact]
    public async Task GetForDeleteAsync_UnknownId_ReturnsNotFound()
    {
        var result = await _service.GetForDeleteAsync(999, _ivan.Id, true);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    // ---------- CreateAsync ----------

    [Fact]
    public async Task CreateAsync_ValidInput_SavesResourceWithOwnerAndRelations()
    {
        var input = ValidInput();
        input.CategoryIds = new List<int> { _math.Id, _music.Id };
        input.GradeLevelIds = new List<int> { _grade5.Id, _grade6.Id };

        var before = DateTime.UtcNow;
        var result = await _service.CreateAsync(input, _maria.Id);

        Assert.True(result.Succeeded);
        var saved = _context.Resources.Single();
        Assert.Equal("maria", saved.OwnerId);
        Assert.True(saved.CreatedOn >= before);
        Assert.Equal(2, saved.Categories.Count);
        Assert.Equal(2, saved.GradeLevels.Count);
    }

    [Fact]
    public async Task CreateAsync_ValidInput_SavesLicenseAndLoadsItInDetails()
    {
        var result = await _service.CreateAsync(ValidInput(), _ivan.Id);

        Assert.Equal(_license.Id, result.Value!.LicenseId);
        var details = await _service.GetDetailsAsync(result.Value.Id);
        Assert.Equal("CC BY 4.0", details!.License!.Name);
    }

    [Fact]
    public async Task CreateAsync_TrimsTextAndNormalizesOptionalFields()
    {
        var input = ValidInput();
        input.Title = "  Дроби  ";
        input.Author = "  Иван  ";
        input.Url = "  https://example.com/x  ";
        input.Source = "   ";
        input.Description = null;

        await _service.CreateAsync(input, _ivan.Id);

        var saved = _context.Resources.Single();
        Assert.Equal("Дроби", saved.Title);
        Assert.Equal("Иван", saved.Author);
        Assert.Equal("https://example.com/x", saved.Url);
        Assert.Null(saved.Source);
        Assert.Equal(string.Empty, saved.Description);
    }

    [Fact]
    public async Task CreateAsync_DuplicateIds_AreSavedOnce()
    {
        var input = ValidInput();
        input.CategoryIds = new List<int> { _math.Id, _math.Id };
        input.GradeLevelIds = new List<int> { _grade5.Id, _grade5.Id };

        var result = await _service.CreateAsync(input, _ivan.Id);

        Assert.True(result.Succeeded);
        Assert.Single(_context.Resources.Single().Categories);
        Assert.Single(_context.Resources.Single().GradeLevels);
    }

    public static IEnumerable<object[]> InvalidInputs()
    {
        yield return new object[] { (Action<ResourceInput>)(i => i.Title = ""), "Заглавието е задължително" };
        yield return new object[] { (Action<ResourceInput>)(i => i.Title = "   "), "Заглавието е задължително" };
        yield return new object[] { (Action<ResourceInput>)(i => i.Title = new string('а', ResourceService.TitleMaxLength + 1)), "Заглавието не може" };
        yield return new object[] { (Action<ResourceInput>)(i => i.Author = ""), "автора" };
        yield return new object[] { (Action<ResourceInput>)(i => i.Author = new string('а', ResourceService.AuthorMaxLength + 1)), "Авторът не може" };
        yield return new object[] { (Action<ResourceInput>)(i => i.Source = new string('а', ResourceService.SourceMaxLength + 1)), "Източникът не може" };
        yield return new object[] { (Action<ResourceInput>)(i => i.Description = new string('а', ResourceService.DescriptionMaxLength + 1)), "Описанието не може" };
        yield return new object[] { (Action<ResourceInput>)(i => i.Url = ""), "Уеб адресът е задължителен" };
        yield return new object[] { (Action<ResourceInput>)(i => i.Url = "https://example.com/" + new string('a', ResourceService.UrlMaxLength)), "Уеб адресът не може" };
        yield return new object[] { (Action<ResourceInput>)(i => i.Url = "javascript:alert(1)"), "http://" };
        yield return new object[] { (Action<ResourceInput>)(i => i.Url = "ftp://example.com/file"), "http://" };
        yield return new object[] { (Action<ResourceInput>)(i => i.Url = "not a url"), "http://" };
        yield return new object[] { (Action<ResourceInput>)(i => i.Url = "/relative/path"), "http://" };
        yield return new object[] { (Action<ResourceInput>)(i => i.LicenseId = null), "Изберете лиценз" };
        yield return new object[] { (Action<ResourceInput>)(i => i.LicenseId = 999), "несъществуващ лиценз" };
        yield return new object[] { (Action<ResourceInput>)(i => i.CategoryIds = new List<int>()), "предмет" };
        yield return new object[] { (Action<ResourceInput>)(i => i.GradeLevelIds = new List<int>()), "клас" };
        yield return new object[] { (Action<ResourceInput>)(i => i.CategoryIds = new List<int> { 999 }), "несъществуващ предмет" };
        yield return new object[] { (Action<ResourceInput>)(i => i.GradeLevelIds = new List<int> { 999 }), "несъществуващ клас" };
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task CreateAsync_InvalidInput_ReturnsInvalidAndSavesNothing(Action<ResourceInput> breakIt, string expectedMessagePart)
    {
        var input = ValidInput();
        breakIt(input);

        var result = await _service.CreateAsync(input, _ivan.Id);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains(expectedMessagePart, result.Message);
        Assert.Empty(_context.Resources);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?x=1")]
    [InlineData("HTTPS://EXAMPLE.COM")]
    public async Task CreateAsync_HttpAndHttpsUrls_AreAccepted(string url)
    {
        var input = ValidInput();
        input.Url = url;

        var result = await _service.CreateAsync(input, _ivan.Id);

        Assert.True(result.Succeeded);
    }

    // ---------- UpdateAsync ----------

    [Fact]
    public async Task UpdateAsync_Owner_ChangesFieldsAndReplacesSelections()
    {
        var resource = await AddResourceAsync("Стар", owner: _ivan, category: _math, grade: _grade5);
        var input = ValidInput();
        input.Title = "Нов";
        input.CategoryIds = new List<int> { _music.Id };
        input.GradeLevelIds = new List<int> { _grade6.Id };

        var result = await _service.UpdateAsync(resource.Id, input, _ivan.Id);

        Assert.True(result.Succeeded);
        var saved = _context.Resources.Single();
        Assert.Equal("Нов", saved.Title);
        Assert.Equal("Музика", saved.Categories.Single().Name);
        Assert.Equal(6, saved.GradeLevels.Single().Number);
    }

    [Fact]
    public async Task UpdateAsync_KeepsOwnerAndCreationDate()
    {
        var created = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        var resource = await AddResourceAsync("Стар", owner: _ivan, createdOn: created);

        await _service.UpdateAsync(resource.Id, ValidInput(), _ivan.Id);

        var saved = _context.Resources.Single();
        Assert.Equal("ivan", saved.OwnerId);
        Assert.Equal(created, saved.CreatedOn);
    }

    [Fact]
    public async Task UpdateAsync_AnotherUser_IsForbiddenAndChangesNothing()
    {
        var resource = await AddResourceAsync("Стар", owner: _ivan);

        var result = await _service.UpdateAsync(resource.Id, ValidInput(), _maria.Id);

        Assert.Equal(ServiceError.Forbidden, result.Error);
        Assert.Equal("Стар", _context.Resources.Single().Title);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNotFound()
    {
        var result = await _service.UpdateAsync(999, ValidInput(), _ivan.Id);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_InvalidInput_ReturnsInvalidAndKeepsData()
    {
        var resource = await AddResourceAsync("Стар", owner: _ivan);
        var input = ValidInput();
        input.Url = "javascript:alert(1)";

        var result = await _service.UpdateAsync(resource.Id, input, _ivan.Id);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("Стар", _context.Resources.Single().Title);
        Assert.Equal("https://example.com", _context.Resources.Single().Url);
    }

    // ---------- DeleteAsync ----------

    [Fact]
    public async Task DeleteAsync_Owner_RemovesResource()
    {
        var resource = await AddResourceAsync("Дроби", owner: _ivan);

        var result = await _service.DeleteAsync(resource.Id, _ivan.Id, isAdministrator: false);

        Assert.True(result.Succeeded);
        Assert.Empty(_context.Resources);
    }

    [Fact]
    public async Task DeleteAsync_Administrator_CanRemoveAnotherUsersResource()
    {
        var resource = await AddResourceAsync("Дроби", owner: _ivan);

        var result = await _service.DeleteAsync(resource.Id, _maria.Id, isAdministrator: true);

        Assert.True(result.Succeeded);
        Assert.Empty(_context.Resources);
    }

    [Fact]
    public async Task DeleteAsync_AnotherUser_IsForbiddenAndKeepsResource()
    {
        var resource = await AddResourceAsync("Дроби", owner: _ivan);

        var result = await _service.DeleteAsync(resource.Id, _maria.Id, isAdministrator: false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
        Assert.Single(_context.Resources);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsNotFound()
    {
        var result = await _service.DeleteAsync(999, _ivan.Id, isAdministrator: true);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }
}
