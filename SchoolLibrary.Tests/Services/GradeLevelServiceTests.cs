using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.GradeLevels;

namespace SchoolLibrary.Tests.Services;

public class GradeLevelServiceTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsGradesOrderedByNumber()
    {
        using var context = TestDbContextFactory.Create();
        context.GradeLevels.AddRange(
            new GradeLevel { Number = 9 },
            new GradeLevel { Number = 5 },
            new GradeLevel { Number = 12 });
        await context.SaveChangesAsync();
        var service = new GradeLevelService(context);

        var result = await service.GetAllAsync();

        Assert.Equal(new[] { 5, 9, 12 }, result.Select(g => g.Number));
    }

    [Fact]
    public async Task GetAllAsync_WithNoGrades_ReturnsEmptyList()
    {
        using var context = TestDbContextFactory.Create();
        var service = new GradeLevelService(context);

        Assert.Empty(await service.GetAllAsync());
    }

    [Fact]
    public async Task GetAllWithCountsAsync_ReturnsGradesOrderedByNumber_WithTheirResourceCounts()
    {
        using var context = TestDbContextFactory.Create();
        var five = new GradeLevel { Number = 5 };
        var six = new GradeLevel { Number = 6 };
        var seven = new GradeLevel { Number = 7 };
        context.GradeLevels.AddRange(seven, five, six);
        context.Resources.AddRange(
            new Resource { Title = "A", Url = "https://a.example", GradeLevels = { five, six } },
            new Resource { Title = "B", Url = "https://b.example", GradeLevels = { five } });
        await context.SaveChangesAsync();
        var service = new GradeLevelService(context);

        var result = await service.GetAllWithCountsAsync();

        Assert.Equal(new[] { (5, 2), (6, 1), (7, 0) }, result.Select(i => (i.GradeLevel.Number, i.ResourceCount)));
    }

    [Fact]
    public async Task GetAllWithCountsAsync_WithNoGrades_ReturnsEmptyList()
    {
        using var context = TestDbContextFactory.Create();

        Assert.Empty(await new GradeLevelService(context).GetAllWithCountsAsync());
    }
}
