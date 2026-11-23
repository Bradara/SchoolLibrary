using SchoolLibrary.Data.Enums;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.ViewModels;

namespace SchoolLibrary.Tests.Web;

public class CatalogQueryTests
{
    private static readonly (int, string)[] Subjects = { (1, "Математика"), (2, "Музика") };
    private static readonly (int, string)[] Grades = { (10, "V клас"), (11, "VI клас") };
    private static readonly (string, string)[] Owners = { ("ivan", "Иван Петров") };

    private static CatalogQuery Full() => new()
    {
        Kind = ResourceKind.Lesson,
        SearchTitle = " дроби ",
        CategoryId = 1,
        GradeLevelId = 10,
        OwnerId = "ivan",
        FromDate = new DateTime(2026, 11, 1),
        ToDate = new DateTime(2026, 11, 23),
        SortOrder = ResourceSortOrder.TitleAsc
    };

    // ---------- ToRouteValues ----------

    [Fact]
    public void ToRouteValues_EmptyQuery_IsEmpty()
    {
        Assert.Empty(new CatalogQuery().ToRouteValues());
        Assert.Empty(new CatalogQuery().ToRouteValues(page: 1));
    }

    [Fact]
    public void ToRouteValues_WritesEveryValue_WithTheNamesOfTheIndexAction()
    {
        var values = (Full() with { All = true }).ToRouteValues(page: 3);

        Assert.Equal(new Dictionary<string, string?>
        {
            ["kind"] = "Lesson",
            ["searchTitle"] = "дроби",
            ["categoryId"] = "1",
            ["gradeLevelId"] = "10",
            ["ownerId"] = "ivan",
            ["fromDate"] = "2026-11-01",
            ["toDate"] = "2026-11-23",
            ["sortOrder"] = "TitleAsc",
            ["all"] = "true",
            ["page"] = "3"
        }, values);
    }

    [Fact]
    public void ToRouteValues_LeavesOutTheDefaultOrder_BlankSearch_AndPageOne()
    {
        var values = new CatalogQuery { SearchTitle = "  ", SortOrder = ResourceSortOrder.DateDesc }.ToRouteValues(page: 1);

        Assert.Empty(values);
    }

    [Fact]
    public void ToRouteValues_DoesNotDependOnTheCulture()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("bg-BG");

            var values = new CatalogQuery { FromDate = new DateTime(2026, 1, 5) }.ToRouteValues();

            Assert.Equal("2026-01-05", values["fromDate"]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }

    // ---------- ShowsTiles / HasFilters / Cleared ----------

    [Fact]
    public void ShowsTiles_WithNothingChosen()
    {
        Assert.True(new CatalogQuery().ShowsTiles);
        Assert.False(new CatalogQuery().HasFilters);
    }

    [Fact]
    public void ShowsTiles_TheKindTheGradeAndTheOrder_KeepTheTiles()
    {
        var query = new CatalogQuery { Kind = ResourceKind.Upload, GradeLevelId = 10, SortOrder = ResourceSortOrder.TitleAsc };

        Assert.True(query.ShowsTiles);
        Assert.True(query.HasFilters);
    }

    public static TheoryData<CatalogQuery> ListQueries => new()
    {
        new CatalogQuery { CategoryId = 1 },
        new CatalogQuery { SearchTitle = "дроби" },
        new CatalogQuery { OwnerId = "ivan" },
        new CatalogQuery { FromDate = new DateTime(2026, 11, 1) },
        new CatalogQuery { ToDate = new DateTime(2026, 11, 1) },
        new CatalogQuery { All = true }
    };

    [Theory]
    [MemberData(nameof(ListQueries))]
    public void ShowsTiles_ASubjectSearchOwnerDateOrAll_ShowsTheResults(CatalogQuery query)
    {
        Assert.False(query.ShowsTiles);
    }

    [Fact]
    public void Cleared_KeepsOnlyTheKindAndTheOrder()
    {
        var cleared = (Full() with { All = true }).Cleared();

        Assert.Equal(new CatalogQuery { Kind = ResourceKind.Lesson, SortOrder = ResourceSortOrder.TitleAsc }, cleared);
        Assert.True(cleared.ShowsTiles);
    }

    // ---------- ActiveFilters ----------

    [Fact]
    public void ActiveFilters_NothingChosen_IsEmpty()
    {
        Assert.Empty(new CatalogQuery { Kind = ResourceKind.Lesson }.ActiveFilters(Subjects, Grades, Owners));
    }

    [Fact]
    public void ActiveFilters_EveryFilter_HasALabel_AndTheQueryWithoutIt()
    {
        var query = Full();

        var filters = query.ActiveFilters(Subjects, Grades, Owners);

        Assert.Equal(
            new[] { "Предмет: Математика", "Клас: V клас", "„дроби“", "Публикувал: Иван Петров", "От 01.11.2026", "До 23.11.2026" },
            filters.Select(f => f.Label));
        Assert.Equal(query with { CategoryId = null }, filters[0].Without);
        Assert.Equal(query with { GradeLevelId = null }, filters[1].Without);
        Assert.Equal(query with { SearchTitle = null }, filters[2].Without);
        Assert.Equal(query with { OwnerId = null }, filters[3].Without);
        Assert.Equal(query with { FromDate = null }, filters[4].Without);
        Assert.Equal(query with { ToDate = null }, filters[5].Without);
        // The kind is not a pill: the kind switch shows it
        Assert.All(filters, f => Assert.Equal(ResourceKind.Lesson, f.Without.Kind));
    }

    [Fact]
    public void ActiveFilters_UnknownIds_AreStillRemovable()
    {
        var filters = new CatalogQuery { CategoryId = 99, GradeLevelId = 98, OwnerId = "gone" }
            .ActiveFilters(Subjects, Grades, Owners);

        Assert.Equal(new[] { "Предмет: ?", "Клас: ?", "Публикувал: ?" }, filters.Select(f => f.Label));
    }

    // ---------- ResourceCount / SubjectTileModel ----------

    [Theory]
    [InlineData(0, "0 ресурса")]
    [InlineData(1, "1 ресурс")]
    [InlineData(2, "2 ресурса")]
    [InlineData(21, "21 ресурса")]
    public void ResourceCount_IsInBulgarian(int count, string expected)
    {
        Assert.Equal(expected, CatalogQuery.ResourceCount(count));
    }

    [Fact]
    public void SubjectTile_LinksToTheSubject_KeepingTheKindAndTheGrade_ButNotAll()
    {
        var query = new CatalogQuery { Kind = ResourceKind.External, GradeLevelId = 10, All = true };
        var tile = new SubjectTileModel(new SubjectFacet(2, "Музика", "bi-music-note-beamed", "#9c36b5", 4), query);

        Assert.Equal(new CatalogQuery { Kind = ResourceKind.External, GradeLevelId = 10, CategoryId = 2 }, tile.Link);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(3)]
    [InlineData(2)]
    public void PageSizes_FillEveryRowOfCards(int columns)
    {
        // The catalog shows 4, 3, 2 or 1 card in a row, depending on the width
        Assert.Equal(0, ResourceFilter.DefaultPageSize % columns);
    }

    [Fact]
    public void NewestSections_AreTwoFullRowsOfFour()
    {
        Assert.Equal(8, ResourceIndexViewModel.NewestOnTiles);
        Assert.Equal(8, HomeIndexViewModel.NewestCount);
    }

    [Fact]
    public void IndexViewModel_Query_CarriesEveryChoice()
    {
        var model = new ResourceIndexViewModel
        {
            Kind = ResourceKind.Lesson,
            SearchTitle = " дроби ",
            CategoryId = 1,
            GradeLevelId = 10,
            OwnerId = "ivan",
            FromDate = new DateTime(2026, 11, 1),
            ToDate = new DateTime(2026, 11, 23),
            SortOrder = ResourceSortOrder.TitleAsc,
            All = true
        };

        Assert.Equal(Full() with { All = true }, model.Query);
    }
}
