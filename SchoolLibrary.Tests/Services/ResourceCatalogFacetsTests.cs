using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

public class ResourceCatalogFacetsTests
{
    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly ResourceService _service;

    private readonly Category _math;
    private readonly Category _music;
    private readonly Category _art;
    private readonly GradeLevel _grade5;
    private readonly GradeLevel _grade6;

    public ResourceCatalogFacetsTests()
    {
        _service = new ResourceService(_context);

        _context.Users.Add(new ApplicationUser { Id = "ivan", UserName = "ivan", FirstName = "Иван", LastName = "Петров" });
        _math = new Category { Name = "Математика", Icon = "bi-plus-slash-minus", Color = "#2f6fed" };
        _music = new Category { Name = "Музика", Icon = "bi-music-note-beamed", Color = "#9c36b5" };
        _art = new Category { Name = "Изобразително изкуство", Icon = "bi-palette", Color = "#e8590c" };
        _context.Categories.AddRange(_math, _music, _art);
        _grade5 = new GradeLevel { Number = 5 };
        _grade6 = new GradeLevel { Number = 6 };
        _context.GradeLevels.AddRange(_grade5, _grade6);
        _context.SaveChanges();
    }

    private void Add(
        string title,
        ResourceKind kind,
        Category[] subjects,
        GradeLevel? grade = null,
        ResourceAudience audience = ResourceAudience.Everyone,
        string? ownerId = "ivan",
        DateTime? createdOn = null)
    {
        var resource = new Resource
        {
            Title = title,
            Author = "Автор",
            Url = "https://example.com/" + Guid.NewGuid(),
            Kind = kind,
            Audience = audience,
            OwnerId = ownerId,
            CreatedOn = createdOn ?? new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        foreach (var subject in subjects)
        {
            resource.Categories.Add(subject);
        }

        resource.GradeLevels.Add(grade ?? _grade5);
        _context.Resources.Add(resource);
        _context.SaveChanges();
    }

    private static Dictionary<string, int> Counts(CatalogFacets facets) =>
        facets.Subjects.ToDictionary(s => s.Name, s => s.Count);

    [Fact]
    public async Task EmptyCatalog_EveryKindIsZero_AndNoSubjects()
    {
        var facets = await _service.GetCatalogFacetsAsync(new ResourceFilter());

        Assert.Equal(Enum.GetValues<ResourceKind>(), facets.ByKind.Keys.Order());
        Assert.All(facets.ByKind.Values, c => Assert.Equal(0, c));
        Assert.Equal(0, facets.Total);
        Assert.Empty(facets.Subjects);
    }

    [Fact]
    public async Task CountsEveryKind_AndEverySubject()
    {
        Add("Дроби", ResourceKind.Lesson, new[] { _math });
        Add("Уравнения", ResourceKind.Lesson, new[] { _math });
        Add("Ноти", ResourceKind.External, new[] { _music });
        Add("Книга", ResourceKind.Upload, new[] { _math, _music });

        var facets = await _service.GetCatalogFacetsAsync(new ResourceFilter());

        Assert.Equal(2, facets.CountOf(ResourceKind.Lesson));
        Assert.Equal(1, facets.CountOf(ResourceKind.External));
        Assert.Equal(1, facets.CountOf(ResourceKind.Upload));
        Assert.Equal(4, facets.Total);
        // A resource in two subjects counts in both
        Assert.Equal(new Dictionary<string, int> { ["Математика"] = 3, ["Музика"] = 2 }, Counts(facets));
    }

    [Fact]
    public async Task SubjectsWithoutResources_AreLeftOut()
    {
        Add("Дроби", ResourceKind.Lesson, new[] { _math });

        var facets = await _service.GetCatalogFacetsAsync(new ResourceFilter());

        Assert.DoesNotContain(facets.Subjects, s => s.Name == _art.Name);
        Assert.DoesNotContain(facets.Subjects, s => s.Name == _music.Name);
    }

    [Fact]
    public async Task Subjects_CarryTheirLook_AndAreInBulgarianOrder()
    {
        Add("Дроби", ResourceKind.Lesson, new[] { _math, _music, _art });

        var facets = await _service.GetCatalogFacetsAsync(new ResourceFilter());

        Assert.Equal(new[] { "Изобразително изкуство", "Математика", "Музика" }, facets.Subjects.Select(s => s.Name));
        Assert.Equal(new SubjectFacet(_math.Id, "Математика", "bi-plus-slash-minus", "#2f6fed", 1), facets.Subjects[1]);
    }

    [Fact]
    public async Task ChosenKind_ChangesTheSubjectCounts_ButNotTheKindCounts()
    {
        Add("Дроби", ResourceKind.Lesson, new[] { _math });
        Add("Ноти", ResourceKind.External, new[] { _music });
        Add("Задачи", ResourceKind.External, new[] { _math });

        var facets = await _service.GetCatalogFacetsAsync(new ResourceFilter { Kind = ResourceKind.External });

        // The switch still shows how many each kind would give
        Assert.Equal(1, facets.CountOf(ResourceKind.Lesson));
        Assert.Equal(2, facets.CountOf(ResourceKind.External));
        Assert.Equal(new Dictionary<string, int> { ["Математика"] = 1, ["Музика"] = 1 }, Counts(facets));
    }

    [Fact]
    public async Task ChosenSubject_ChangesTheKindCounts_ButNotTheSubjectCounts()
    {
        Add("Дроби", ResourceKind.Lesson, new[] { _math });
        Add("Ноти", ResourceKind.External, new[] { _music });
        Add("Песни", ResourceKind.Lesson, new[] { _music });

        var facets = await _service.GetCatalogFacetsAsync(new ResourceFilter { CategoryId = _music.Id });

        Assert.Equal(1, facets.CountOf(ResourceKind.Lesson));
        Assert.Equal(1, facets.CountOf(ResourceKind.External));
        // The other subjects stay on the tiles with their own numbers
        Assert.Equal(new Dictionary<string, int> { ["Математика"] = 1, ["Музика"] = 2 }, Counts(facets));
    }

    [Fact]
    public async Task ChosenGrade_AppliesToBoth()
    {
        Add("Дроби", ResourceKind.Lesson, new[] { _math }, _grade5);
        Add("Проценти", ResourceKind.Lesson, new[] { _math }, _grade6);
        Add("Ноти", ResourceKind.External, new[] { _music }, _grade6);

        var facets = await _service.GetCatalogFacetsAsync(new ResourceFilter { GradeLevelId = _grade5.Id });

        Assert.Equal(1, facets.Total);
        Assert.Equal(new Dictionary<string, int> { ["Математика"] = 1 }, Counts(facets));
    }

    [Fact]
    public async Task SearchOwnerAndDates_ApplyToBoth()
    {
        Add("Дроби", ResourceKind.Lesson, new[] { _math }, createdOn: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        Add("Дроби и проценти", ResourceKind.External, new[] { _math }, ownerId: null);
        Add("Ноти", ResourceKind.Lesson, new[] { _music });

        Assert.Equal(2, (await _service.GetCatalogFacetsAsync(new ResourceFilter { SearchText = "дроби" })).Total);
        Assert.Equal(2, (await _service.GetCatalogFacetsAsync(new ResourceFilter { OwnerId = "ivan" })).Total);

        var fromNovember = await _service.GetCatalogFacetsAsync(new ResourceFilter { FromDate = new DateTime(2026, 11, 1) });
        Assert.Equal(2, fromNovember.Total);
        Assert.Equal(new Dictionary<string, int> { ["Математика"] = 1, ["Музика"] = 1 }, Counts(fromNovember));
    }

    [Fact]
    public async Task HiddenResources_AreNotCounted()
    {
        Add("За всички", ResourceKind.Lesson, new[] { _math });
        Add("Само за учители", ResourceKind.Lesson, new[] { _math, _art }, audience: ResourceAudience.Teachers);

        var guest = await _service.GetCatalogFacetsAsync(new ResourceFilter());
        Assert.Equal(1, guest.Total);
        // A subject whose only resource is hidden is not shown at all
        Assert.Equal(new Dictionary<string, int> { ["Математика"] = 1 }, Counts(guest));

        var teacher = await _service.GetCatalogFacetsAsync(new ResourceFilter { Viewer = TestViewers.Teacher("maria") });
        Assert.Equal(2, teacher.Total);
        Assert.Equal(new Dictionary<string, int> { ["Изобразително изкуство"] = 1, ["Математика"] = 2 }, Counts(teacher));
    }

    [Fact]
    public async Task PagingAndSorting_AreIgnored()
    {
        for (var i = 0; i < 15; i++)
        {
            Add("Урок " + i, ResourceKind.Lesson, new[] { _math });
        }

        var facets = await _service.GetCatalogFacetsAsync(new ResourceFilter { Page = 2, PageSize = 5, SortOrder = ResourceSortOrder.TitleAsc });

        Assert.Equal(15, facets.Total);
        Assert.Equal(15, facets.Subjects.Single().Count);
    }

    [Fact]
    public async Task TheFilterOfTheCaller_IsNotChanged()
    {
        var filter = new ResourceFilter { Kind = ResourceKind.Upload, CategoryId = _math.Id };

        await _service.GetCatalogFacetsAsync(filter);

        Assert.Equal(ResourceKind.Upload, filter.Kind);
        Assert.Equal(_math.Id, filter.CategoryId);
    }

    [Fact]
    public async Task WithoutAFilter_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetCatalogFacetsAsync(null!));
    }

    [Fact]
    public void CountOf_UnknownKind_IsZero()
    {
        var facets = new CatalogFacets(new Dictionary<ResourceKind, int>(), Array.Empty<SubjectFacet>());

        Assert.Equal(0, facets.CountOf(ResourceKind.Lesson));
        Assert.Equal(0, facets.Total);
    }
}
